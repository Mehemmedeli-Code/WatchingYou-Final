using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Catalog.Infrastructure;

internal sealed class CatalogApi(CatalogDbContext db) : ICatalogApi
{
    public async Task<MovieSummary?> GetMovieAsync(Guid movieId, CancellationToken ct = default) =>
        await db.Movies
            .AsNoTracking()
            .Where(m => m.Id == movieId)
            .Select(m => new MovieSummary(m.Id, m.Title, m.ReleaseYear, m.Genre, m.DailyPrice, m.AvailableCopies, m.PosterUrl))
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Decrements stock with a conditional UPDATE rather than read-then-write. The
    /// database evaluates "AvailableCopies > 0" and the decrement in one atomic
    /// statement, so two customers racing for the last copy cannot both win.
    /// </summary>
    public async Task<bool> TryReserveCopyAsync(Guid movieId, CancellationToken ct = default)
    {
        var affected = await db.Movies
            .Where(m => m.Id == movieId && m.AvailableCopies > 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.AvailableCopies, m => m.AvailableCopies - 1), ct);

        return affected == 1;
    }

    public async Task ReleaseCopyAsync(Guid movieId, CancellationToken ct = default) =>
        await db.Movies
            .Where(m => m.Id == movieId && m.AvailableCopies < m.TotalCopies)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.AvailableCopies, m => m.AvailableCopies + 1), ct);

    public async Task RecalculateRatingAsync(Guid movieId, CancellationToken ct = default)
    {
        var movie = await db.Movies.Include(m => m.Reviews).FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null || movie.Reviews.Count == 0) return;

        movie.ReviewCount = movie.Reviews.Count;
        movie.AverageRating = Math.Round(movie.Reviews.Average(r => (double)r.Stars), 2);
        await db.SaveChangesAsync(ct);
    }
}
