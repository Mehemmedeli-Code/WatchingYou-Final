using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Cqrs;

namespace MovieRental.Modules.Media.Features;

/// <summary>
/// The two public galleries. Origin decides which one a film lands in; the filter here is
/// the single place that decides what a stranger is allowed to see.
/// </summary>
public sealed record GetGalleryQuery(ShortFilmOrigin Origin) : IQuery<IReadOnlyList<ShortFilmSummary>>;

internal sealed class GetGalleryHandler(MediaDbContext db)
    : IQueryHandler<GetGalleryQuery, IReadOnlyList<ShortFilmSummary>>
{
    public async Task<IReadOnlyList<ShortFilmSummary>> Handle(GetGalleryQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var films = await db.ShortFilms.AsNoTracking()
            .Where(f => f.Origin == query.Origin
                     && f.Status == SubmissionStatus.Approved
                     && f.Visibility == ShortFilmVisibility.Public)
            .OrderByDescending(f => f.ApprovedAtUtc)
            .Take(60)
            .ToListAsync(ct);

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
