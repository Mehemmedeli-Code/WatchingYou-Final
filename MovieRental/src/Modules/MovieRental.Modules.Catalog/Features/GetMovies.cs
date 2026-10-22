using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;

namespace MovieRental.Modules.Catalog.Features;

// Feature 2 + 10 — browsing with search, multi-criteria filters, sorting and paging.
// Bound straight off the query string with [AsParameters].
public readonly record struct MovieFilterRequest(
    string? Search, string? Genre, int? YearFrom, int? YearTo, double? MinRating,
    bool? OnlyAvailable, string? SortBy, string? SortDir, int? Page, int? PageSize, bool? IncludeDeleted);

public sealed record GetMoviesQuery(MovieFilterRequest Filter) : IQuery<PagedResult<MovieListItem>>;

internal sealed class GetMoviesHandler(CatalogDbContext db)
    : IQueryHandler<GetMoviesQuery, PagedResult<MovieListItem>>
{
    private const int MaxPageSize = 60;

    public async Task<PagedResult<MovieListItem>> Handle(GetMoviesQuery query, CancellationToken ct)
    {
        var f = query.Filter;
        var page = Math.Max(1, f.Page ?? 1);
        var pageSize = Math.Clamp(f.PageSize ?? 12, 1, MaxPageSize);

        // AsNoTracking: this is a read slice, the change tracker would only cost memory.
        IQueryable<Movie> movies = db.Movies.AsNoTracking();

        // The admin restore screen is the one caller allowed to see soft-deleted rows.
        if (f.IncludeDeleted == true) movies = movies.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = f.Search.Trim();
            movies = movies.Where(m =>
                EF.Functions.Like(m.Title, $"%{term}%") ||
                EF.Functions.Like(m.Description, $"%{term}%") ||
                (m.Director != null && EF.Functions.Like(m.Director, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(f.Genre) && !f.Genre.Equals("all", StringComparison.OrdinalIgnoreCase))
            movies = movies.Where(m => m.Genre == f.Genre);

        if (f.YearFrom is { } from) movies = movies.Where(m => m.ReleaseYear >= from);
        if (f.YearTo is { } to) movies = movies.Where(m => m.ReleaseYear <= to);
        if (f.MinRating is { } minRating) movies = movies.Where(m => m.AverageRating >= minRating);
        if (f.OnlyAvailable == true) movies = movies.Where(m => m.AvailableCopies > 0);

        var descending = string.Equals(f.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        movies = (f.SortBy?.ToLowerInvariant()) switch
        {
            "title" => descending ? movies.OrderByDescending(m => m.Title) : movies.OrderBy(m => m.Title),
            "year" => descending ? movies.OrderByDescending(m => m.ReleaseYear) : movies.OrderBy(m => m.ReleaseYear),
            "price" => descending ? movies.OrderByDescending(m => m.DailyPrice) : movies.OrderBy(m => m.DailyPrice),
            "rating" => descending ? movies.OrderByDescending(m => m.AverageRating) : movies.OrderBy(m => m.AverageRating),
            _ => descending ? movies.OrderBy(m => m.CreatedAtUtc) : movies.OrderByDescending(m => m.CreatedAtUtc)
        };

        // Count before paging, then a single round trip for the page itself.
        var total = await movies.CountAsync(ct);
        var items = await movies
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MovieListItem(
                m.Id, m.Title, m.Slug, m.Genre, m.ReleaseYear, m.DurationMinutes,
                m.DailyPrice, m.AvailableCopies, m.TotalCopies, m.AverageRating, m.ReviewCount,
                m.PosterUrl, m.IsDeleted))
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
                [AsParameters] MovieFilterRequest filter, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetMoviesQuery(filter), ct)))
            .WithName("GetMovies").WithTags("Catalog").AllowAnonymous();

        app.MapGet("/api/movies/genres", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGenresQuery(), ct)))
            .WithName("GetGenres").WithTags("Catalog").AllowAnonymous();
    }
}
