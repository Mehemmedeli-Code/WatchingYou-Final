using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;
using MovieRental.SharedKernel.Results;

namespace MovieRental.Modules.Catalog.Features;

// Feature 2 + 10 — browsing with search, multi-criteria filters, sorting and paging.
// Bound straight off the query string with [AsParameters].
public readonly record struct MovieFilterRequest(
    string? Search, string? Genre, int? YearFrom, int? YearTo, double? MinRating,
    bool? OnlyAvailable, string? SortBy, string? SortDir, int? Page, int? PageSize, bool? IncludeDeleted,
    bool? Originals = null);

public sealed record GetMoviesQuery(MovieFilterRequest Filter) : IQuery<PagedResult<MovieListItem>>;

internal sealed class GetMoviesHandler(CatalogDbContext db)
    : IQueryHandler<GetMoviesQuery, PagedResult<MovieListItem>>
{
    private const int MaxPageSize = 60;

    public async Task<PagedResult<MovieListItem>> Handle(GetMoviesQuery query, CancellationToken ct)
    {
        var f = query.Filter;
        var page = Math.Clamp(f.Page ?? 1, 1, 10_000);
        var pageSize = Math.Clamp(f.PageSize ?? 12, 1, MaxPageSize);

        // AsNoTracking: this is a read slice, the change tracker would only cost memory.
        IQueryable<Movie> movies = db.Movies.AsNoTracking();

        // The admin restore screen is the one caller allowed to see soft-deleted rows.
        if (f.IncludeDeleted == true) movies = movies.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            // Contains, not LIKE with the text pasted in: % and _ typed by a visitor stayed
            // wildcards, and a handful of them kept the database busy for the full 30 seconds.
            var term = f.Search.Trim();
            if (term.Length > 100) term = term[..100];
            movies = movies.Where(m =>
                m.Title.Contains(term) ||
                m.Description.Contains(term) ||
                (m.Director != null && m.Director.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(f.Genre) && !f.Genre.Equals("all", StringComparison.OrdinalIgnoreCase))
            movies = movies.Where(m => m.Genre == f.Genre);

        if (f.YearFrom is { } from) movies = movies.Where(m => m.ReleaseYear >= from);
        if (f.YearTo is { } to) movies = movies.Where(m => m.ReleaseYear <= to);
        if (f.MinRating is { } minRating && double.IsFinite(minRating)) movies = movies.Where(m => m.AverageRating >= minRating);
        if (f.OnlyAvailable == true) movies = movies.Where(m => m.AvailableCopies > 0);
        if (f.Originals is { } originals) movies = movies.Where(m => m.IsOriginal == originals);

        var descending = string.Equals(f.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        // Real films first whatever the sort: our own Originals never crowd the first page of the
        // catalogue; they follow once the released films run out.
        var realFirst = movies.OrderBy(m => m.IsOriginal);
        movies = (f.SortBy?.ToLowerInvariant()) switch
        {
            "title" => descending ? realFirst.ThenByDescending(m => m.Title) : realFirst.ThenBy(m => m.Title),
            "year" => descending ? realFirst.ThenByDescending(m => m.ReleaseYear) : realFirst.ThenBy(m => m.ReleaseYear),
            "price" => descending ? realFirst.ThenByDescending(m => m.DailyPrice) : realFirst.ThenBy(m => m.DailyPrice),
            "rating" => descending ? realFirst.ThenByDescending(m => m.AverageRating) : realFirst.ThenBy(m => m.AverageRating),
            _ => descending ? realFirst.ThenBy(m => m.CreatedAtUtc) : realFirst.ThenByDescending(m => m.CreatedAtUtc)
        };

        // Count before paging, then a single round trip for the page itself.
        var total = await movies.CountAsync(ct);
        var items = await movies
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MovieListItem(
                m.Id, m.Title, m.Slug, m.Genre, m.ReleaseYear, m.DurationMinutes,
                m.DailyPrice, m.AvailableCopies, m.TotalCopies, m.AverageRating, m.ReviewCount,
                m.PosterUrl, m.IsDeleted, m.VideoUrl != null && m.VideoUrl != "", m.TrailerUrl, m.IsOriginal))
            .ToListAsync(ct);

        return new PagedResult<MovieListItem>(items, page, pageSize, total);
    }
}

public sealed record GetGenresQuery : IQuery<IReadOnlyList<string>>;

internal sealed class GetGenresHandler(CatalogDbContext db) : IQueryHandler<GetGenresQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GetGenresQuery query, CancellationToken ct) =>
        await db.Movies.AsNoTracking().Select(m => m.Genre).Distinct().OrderBy(g => g).ToListAsync(ct);
}

public static class GetMoviesEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/movies", async (
                [AsParameters] MovieFilterRequest filter, IDispatcher dispatcher, ICurrentUser user, CancellationToken ct) =>
            {
                // Withdrawn titles are for the admin's restore screen only. The flag used to be
                // honoured for anyone who added it to the query string.
                var safe = filter with { IncludeDeleted = filter.IncludeDeleted == true && user.IsInRole(AppRoles.Admin) };
                return Results.Ok(await dispatcher.Ask(new GetMoviesQuery(safe), ct));
            })
            .WithName("GetMovies").WithTags("Catalog").AllowAnonymous().CacheOutput(AppPolicies.CatalogueCache);

        app.MapGet("/api/movies/genres", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGenresQuery(), ct)))
            .WithName("GetGenres").WithTags("Catalog").AllowAnonymous().CacheOutput(AppPolicies.CatalogueCache);
    }
}
