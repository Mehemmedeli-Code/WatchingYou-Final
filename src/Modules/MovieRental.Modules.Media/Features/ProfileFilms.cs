using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

// The film grid on a profile, in place of Instagram's posts: the member's approved films, for
// whoever may open the profile (anyone on a public account; on a private one, the people it
// accepted). Each film says whether it is also out in its gallery (Listed), which on a private
// account is the owner's choice per film. Films still in review stay in Studio.
/// <summary>One square of the grid. No reviewer notes or file names: other people see this.</summary>
public sealed record ProfileFilm(
    Guid Id, string Title, string Synopsis, ShortFilmOrigin Origin, ShortFilmVisibility Visibility,
    SubmissionStatus Status, int ViewCount, DateTime SubmittedAtUtc, string StreamUrl);

public sealed record ProfileFilms(IReadOnlyList<ProfileFilm> Items, int PublicCount);

/// <param name="Origin">ai or human; null for both.</param>
public sealed record GetProfileFilmsQuery(Guid UserId, string? Origin) : IQuery<Result<ProfileFilms>>;

internal sealed class GetProfileFilmsHandler(MediaDbContext db, ICurrentUser currentUser, IUserDirectory users)
    : IQueryHandler<GetProfileFilmsQuery, Result<ProfileFilms>>
{
    public async Task<Result<ProfileFilms>> Handle(GetProfileFilmsQuery query, CancellationToken ct)
    {
        if (!await users.MayViewProfileAsync(currentUser.RequireId(), query.UserId, ct))
            return Result.Failure<ProfileFilms>(Error.Forbidden("This account is private. Follow it and wait to be accepted to see its films."));

        var films = db.ShortFilms.AsNoTracking().Where(f => f.UserId == query.UserId && f.Status == SubmissionStatus.Approved);
        films = query.Origin?.ToLowerInvariant() switch
        {
            "ai" => films.Where(f => f.Origin == ShortFilmOrigin.AiGenerated),
            "human" => films.Where(f => f.Origin == ShortFilmOrigin.HandCrafted),
            _ => films
        };

        // ponytail: newest 120, no paging; page it when someone actually posts that many.
        var items = await films.OrderByDescending(f => f.ApprovedAtUtc).Take(120)
            .Select(f => new ProfileFilm(f.Id, f.Title, f.Synopsis, f.Origin, f.Visibility, f.Status,
                f.ViewCount, f.SubmittedAtUtc, "/api/shorts/" + f.Id + "/stream"))
            .ToListAsync(ct);
        var count = await db.ShortFilms.CountAsync(f => f.UserId == query.UserId && f.Status == SubmissionStatus.Approved, ct);
        return new ProfileFilms(items, count);
    }
}

public static class ProfileFilmsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/shorts/by/{userId:guid}", async (Guid userId, string? origin, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Ask(new GetProfileFilmsQuery(userId, origin), ct)).ToHttp())
            .WithName("GetProfileFilms").WithTags("Shorts").RequireAuthorization()
            .WithSummary("Someone's approved films, for their profile")
            .WithDescription("`origin`: ai or human; left out, both. 403: a private account that has not accepted you.")
            .Produces<ProfileFilms>()
            .Produces<Error>(StatusCodes.Status403Forbidden);
}