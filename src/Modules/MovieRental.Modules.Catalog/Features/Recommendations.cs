using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

// "For you" — films picked from what this customer has rented, saved and reviewed.
// The scoring lives in Domain/RecommendationEngine.cs; this slice only gathers the inputs.

/// <param name="Personal">False when there was no history to go on and the list is simply
/// the best-reviewed films. The page says so rather than pretending to know the visitor.</param>
public sealed record RecommendationList(bool Personal, IReadOnlyList<Recommendation> Items);

/// <param name="BecauseGenre">The liked genre that earned the film its place, or null when it
/// is there on quality alone.</param>
public sealed record Recommendation(MovieListItem Movie, double Score, string? BecauseGenre);

public sealed record GetRecommendationsQuery(int Take) : IQuery<RecommendationList>;

internal sealed class GetRecommendationsHandler(CatalogDbContext db, ICurrentUser currentUser, IRentalApi rentals)
    : IQueryHandler<GetRecommendationsQuery, RecommendationList>
{
    /// <summary>How many of the best-reviewed films are considered at all. The scoring is
    /// in memory, so the pool is bounded; beyond a couple of hundred it only adds noise.</summary>
    private const int CandidatePool = 250;

    public async Task<RecommendationList> Handle(GetRecommendationsQuery query, CancellationToken ct)
    {
        var take = Math.Clamp(query.Take, 1, 24);
        var signals = new List<TasteSignal>();
        var exclude = new HashSet<Guid>();

        if (currentUser.Id is { } userId)
        {
            var rented = await rentals.RentedMovieIdsAsync(userId, ct);
            var saved = await db.Watchlist.AsNoTracking()
                .Where(w => w.UserId == userId).Select(w => w.MovieId).ToListAsync(ct);
            var reviews = await db.Reviews.AsNoTracking()
                .Where(r => r.UserId == userId).Select(r => new { r.MovieId, r.Stars }).ToListAsync(ct);

            var known = rented.Concat(saved).Concat(reviews.Select(r => r.MovieId)).Distinct().ToArray();
            var genres = await db.Movies.AsNoTracking().IgnoreQueryFilters()
                .Where(m => known.Contains(m.Id))
                .Select(m => new { m.Id, m.Genre })
                .ToDictionaryAsync(m => m.Id, m => m.Genre, ct);

            foreach (var id in rented)
                if (genres.TryGetValue(id, out var g)) signals.Add(new TasteSignal(g, TasteSignalKind.Rented));
            foreach (var id in saved)
                if (genres.TryGetValue(id, out var g)) signals.Add(new TasteSignal(g, TasteSignalKind.Watchlisted));
            foreach (var review in reviews)
                if (genres.TryGetValue(review.MovieId, out var g)) signals.Add(new TasteSignal(g, TasteSignalKind.Reviewed, review.Stars));

            // Already seen or already saved: suggesting those back is not a recommendation.
            exclude.UnionWith(rented);
            exclude.UnionWith(saved);
        }

        var candidates = await db.Movies.AsNoTracking()
            .OrderByDescending(m => m.AverageRating).ThenByDescending(m => m.ReviewCount)
            .Take(CandidatePool)
            .Select(m => new RecommendationCandidate(m.Id, m.Genre, m.AverageRating, m.ReviewCount))
            .ToListAsync(ct);

        var ranked = RecommendationEngine.Rank(candidates, signals, exclude, take);
        var ids = ranked.Select(r => r.Id).ToArray();

        var movies = await db.Movies.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new MovieListItem(
                m.Id, m.Title, m.Slug, m.Genre, m.ReleaseYear, m.DurationMinutes,
                m.DailyPrice, m.AvailableCopies, m.TotalCopies, m.AverageRating, m.ReviewCount,
                m.PosterUrl, m.IsDeleted, m.VideoUrl != null && m.VideoUrl != "", m.TrailerUrl))
            .ToDictionaryAsync(m => m.Id, ct);

        var items = ranked
            .Where(r => movies.ContainsKey(r.Id))
            .Select(r => new Recommendation(movies[r.Id], r.Score, r.BecauseGenre))
            .ToList();

        return new RecommendationList(signals.Count > 0, items);
    }
}

public static class RecommendationsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        // Anonymous visitors get the quality ordering; a signed-in one gets their own list.
        app.MapGet("/api/recommendations", async (int? take, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetRecommendationsQuery(take ?? 8), ct)))
            .WithName("GetRecommendations").WithTags("Catalog").AllowAnonymous();
}
