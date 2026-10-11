using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;

namespace MovieRental.Modules.Media.Features;

/// <summary>
/// The two public galleries. Origin decides which one a film lands in; ViewerRules.IsListed
/// decides which approved films a stranger may see.
/// </summary>
public sealed record GetGalleryQuery(ShortFilmOrigin Origin) : IQuery<IReadOnlyList<ShortFilmSummary>>;

internal sealed class GetGalleryHandler(MediaDbContext db, IUserDirectory users)
    : IQueryHandler<GetGalleryQuery, IReadOnlyList<ShortFilmSummary>>
{
    public async Task<IReadOnlyList<ShortFilmSummary>> Handle(GetGalleryQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Approved films, newest first; then the ones a private account kept for its followers
        // are dropped (ViewerRules.IsListed). A few extra are read so the page still fills.
        // ponytail: over-reads 3x and filters in memory; move the rule into SQL if private
        // accounts ever hold most of the approved films.
        var candidates = await db.ShortFilms.AsNoTracking()
            .Where(f => f.Origin == query.Origin && f.Status == SubmissionStatus.Approved)
            .OrderByDescending(f => f.ApprovedAtUtc)
            .Take(180)
            .ToListAsync(ct);
        var privateOwners = await users.PrivateAccountsAsync([.. candidates.Select(f => f.UserId).Distinct()], ct);
        var films = candidates.Where(f => ViewerRules.IsListed(f, privateOwners.Contains(f.UserId))).Take(60);
        return [.. films.Select(f => f.ToSummary(now))];
    }
}

public static class GalleryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/gallery/ai", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGalleryQuery(ShortFilmOrigin.AiGenerated), ct)))
            .WithName("GetAiCatalog").WithTags("Galleries").AllowAnonymous();

        app.MapGet("/api/gallery/human", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGalleryQuery(ShortFilmOrigin.HandCrafted), ct)))
            .WithName("GetHumanCraft").WithTags("Galleries").AllowAnonymous();
    }
}
