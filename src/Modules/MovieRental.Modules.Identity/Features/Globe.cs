using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Who is watching, and where.
//
// Two rules shape this whole slice:
//
//  1. Nobody appears without asking to. Registering puts you nowhere; ShareOnGlobe is off
//     until you turn it on, and turning it off removes you immediately.
//  2. The pin is the city, not the person. Coordinates are the city's, identical for everyone
//     in it, and the browser is never asked for a precise location. A map that could point at
//     somebody's street is a different and much worse product.

public sealed record GlobeCity(
    string City, string? CountryCode, double Latitude, double Longitude,
    int MemberCount, IReadOnlyList<GlobeFace> Faces);

/// <summary>Three faces per city, and a count for the rest. A city with a million members
/// still sends four numbers and three URLs — the globe never learns how big it is.</summary>
public sealed record GlobeFace(Guid UserId, string DisplayName, string? AvatarUrl);

public sealed record GlobeMember(
    Guid UserId, string DisplayName, string? AvatarUrl, DateTime JoinedAtUtc);

public sealed record GlobeMemberPage(
    string City, int Total, int Page, int PageSize, IReadOnlyList<GlobeMember> Items);

public sealed record GetGlobeCitiesQuery : IQuery<IReadOnlyList<GlobeCity>>;

internal sealed class GetGlobeCitiesHandler(IdentityDbContext db)
    : IQueryHandler<GetGlobeCitiesQuery, IReadOnlyList<GlobeCity>>
{
    public async Task<IReadOnlyList<GlobeCity>> Handle(GetGlobeCitiesQuery query, CancellationToken ct)
    {
        // Aggregated in SQL. Sending one marker per person is what makes a globe freeze at
        // scale; sending one per city means the payload grows with the number of cities,
        // which is a few hundred, not a few million.
        var cities = await db.Users.AsNoTracking()
            .Where(u => u.ShareOnGlobe && !u.IsSuspended && u.City != null
                     && u.Latitude != null && u.Longitude != null)
            // By name and country only: grouping on the coordinates too split one city in two
            // whenever two people had saved it a few decimals apart ("Baku" twice).
            .GroupBy(u => new { u.City, u.CountryCode })
            .Select(g => new
            {
                g.Key.City,
                g.Key.CountryCode,
                Latitude = g.Max(u => u.Latitude!.Value),
                Longitude = g.Max(u => u.Longitude!.Value),
                Count = g.Count()
            })
            .OrderByDescending(c => c.Count)
            .Take(400)
            .ToListAsync(ct);

        // Three faces per city in one query (ranked per city in SQL), not one query per city —
        // up to 400 round trips every time the globe opened.
        var names = cities.Select(c => c.City).ToList();
        var faces = await db.Users.AsNoTracking()
            .Where(u => u.ShareOnGlobe && !u.IsSuspended && u.City != null && names.Contains(u.City))
            .GroupBy(u => u.City)
            .Select(g => new
            {
                City = g.Key,
                Top = g.OrderByDescending(u => u.LastLoginAtUtc ?? u.CreatedAtUtc).Take(3)
                    .Select(u => new GlobeFace(u.Id, u.FullName, u.AvatarUrl)).ToList()
            })
            .ToDictionaryAsync(x => x.City!, x => x.Top, ct);

        return [.. cities.Select(city => new GlobeCity(city.City!, city.CountryCode, city.Latitude, city.Longitude,
            city.Count, faces.GetValueOrDefault(city.City!) ?? []))];
    }
}

public sealed record GetGlobeMembersQuery(string City, int Page, int PageSize) : IQuery<GlobeMemberPage>;

internal sealed class GetGlobeMembersHandler(IdentityDbContext db)
    : IQueryHandler<GetGlobeMembersQuery, GlobeMemberPage>
{
    public async Task<GlobeMemberPage> Handle(GetGlobeMembersQuery query, CancellationToken ct)
    {
        var size = Math.Clamp(query.PageSize, 1, 50);
        var page = Math.Clamp(query.Page, 1, 10_000);

        var people = db.Users.AsNoTracking()
            .Where(u => u.ShareOnGlobe && !u.IsSuspended && u.City == query.City);

        var total = await people.CountAsync(ct);

        // Paged, not streamed in full: the panel scrolls, so it asks for the next fifty when
        // it needs them rather than downloading a city.
        var items = await people
            .OrderByDescending(u => u.LastLoginAtUtc ?? u.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(u => new GlobeMember(u.Id, u.FullName, u.AvatarUrl, u.CreatedAtUtc))
            .ToListAsync(ct);

        return new GlobeMemberPage(query.City, total, page, size, items);
    }
}

public sealed record UpdateGlobePresenceCommand(
    bool ShareOnGlobe, string? City, string? CountryCode,
    double? Latitude, double? Longitude, string? AvatarUrl) : ICommand<Result>;

internal sealed class UpdateGlobePresenceValidator : AbstractValidator<UpdateGlobePresenceCommand>
{
    public UpdateGlobePresenceValidator()
    {
        RuleFor(x => x.City).NotEmpty().MaximumLength(120).When(x => x.ShareOnGlobe)
            .WithMessage("Choose a city before appearing on the globe.");
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
        RuleFor(x => x.AvatarUrl).MaximumLength(500);
        RuleFor(x => x.CountryCode).Matches("^[A-Za-z]{2}$").When(x => !string.IsNullOrEmpty(x.CountryCode))
            .WithMessage("Country code must be two letters.");
    }
}

internal sealed class UpdateGlobePresenceHandler(IdentityDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateGlobePresenceCommand, Result>
{
    public async Task<Result> Handle(UpdateGlobePresenceCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        user.ShareOnGlobe = command.ShareOnGlobe;
        // AvatarUrl in the body is ignored: the picture belongs to the avatar endpoints. Taking it
        // from here let anyone point their picture at a stranger's server (every viewer's browser
        // then called it) or at another member's file, which removing "their own" picture deleted.

        if (command.ShareOnGlobe)
        {
            user.City = command.City?.Trim();
            user.CountryCode = command.CountryCode?.Trim().ToUpperInvariant();
            // The pin is the city, never the person: whatever the client sends is rounded to a
            // tenth of a degree (about 10 km) before it is stored or shown to anyone.
            user.Latitude = command.Latitude is { } lat ? Math.Round(lat, 1) : null;
            user.Longitude = command.Longitude is { } lon ? Math.Round(lon, 1) : null;
        }
        else
        {
            // Opting out clears the location rather than merely hiding it. A row that still
            // holds where somebody lives is still holding it.
            user.City = null;
            user.CountryCode = null;
            user.Latitude = null;
            user.Longitude = null;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class GlobeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Signed in only. An anonymous visitor has no business browsing who uses the site.
        app.MapGet("/api/globe/cities", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGlobeCitiesQuery(), ct)))
            .WithName("GetGlobeCities").WithTags("Globe").RequireAuthorization();

        app.MapGet("/api/globe/members", async (
                string city, int? page, int? pageSize, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGlobeMembersQuery(city, page ?? 1, pageSize ?? 24), ct)))
            .WithName("GetGlobeMembers").WithTags("Globe").RequireAuthorization();

        app.MapPut("/api/globe/presence",
            async Task<Results<NoContent, BadRequest<Error>>> (
                UpdateGlobePresenceCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.BadRequest(result.Error);
            })
            .WithName("UpdateGlobePresence").WithTags("Globe").RequireAuthorization();

        // Who is online in one country. Only people who chose to be on the globe count, and
        // only the number travels until somebody asks for the list.
        // ponytail: filters the in-memory online set against the table; keep per-country
        // counters in the presence store once millions are online at the same time.
        app.MapGet("/api/globe/online", async (string country, IdentityDbContext db, Presence presence, CancellationToken ct) =>
            {
                var code = country.Trim().ToUpperInvariant();
                var online = presence.OnlineIds();
                var count = await db.Users.AsNoTracking()
                    .CountAsync(u => u.ShareOnGlobe && !u.IsSuspended && u.CountryCode == code && online.Contains(u.Id), ct);
                return Results.Ok(new { country = code, online = count });
            })
            .WithName("GetGlobeOnline").WithTags("Globe").RequireAuthorization();

        app.MapGet("/api/globe/online/members", async (string country, int? page, IdentityDbContext db, Presence presence, CancellationToken ct) =>
            {
                var code = country.Trim().ToUpperInvariant();
                var pageNo = Math.Clamp(page ?? 1, 1, 10_000);
                const int size = 30;
                var online = presence.OnlineIds();
                var people = db.Users.AsNoTracking()
                    .Where(u => u.ShareOnGlobe && !u.IsSuspended && u.CountryCode == code && online.Contains(u.Id));
                var total = await people.CountAsync(ct);
                var items = await people.OrderBy(u => u.Username).ThenBy(u => u.FullName)
                    .Skip((pageNo - 1) * size).Take(size)
                    .Select(u => new { userId = u.Id, displayName = u.FullName, u.Username, u.AvatarUrl, u.City })
                    .ToListAsync(ct);
                return Results.Ok(new { country = code, total, page = pageNo, pageSize = size, items });
            })
            .WithName("GetGlobeOnlineMembers").WithTags("Globe").RequireAuthorization();
    }
}
