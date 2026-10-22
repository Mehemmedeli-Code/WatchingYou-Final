using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Catalog.Infrastructure;

internal sealed class CatalogAnalytics(CatalogDbContext db) : ICatalogAnalytics
{
    public async Task<ChartSeries> TopRatedAsync(int take, CancellationToken ct = default)
    {
        // Only titles that have actually been reviewed; a single five-star review would
        // otherwise sit above a film with fifty reviews averaging 4.6.
        var rows = await db.Movies
            .Where(m => m.ReviewCount > 0)
            .OrderByDescending(m => m.AverageRating)
            .ThenByDescending(m => m.ReviewCount)
            .Take(take)
            .Select(m => new { m.Title, m.AverageRating })
            .ToListAsync(ct);

        return new ChartSeries(
            "top-rated", "Best reviewed", "avg stars",
            "Average score of titles with at least one review.",
            [.. rows.Select(r => new ChartPoint(Shorten(r.Title), Math.Round(r.AverageRating, 1)))]);
    }

    internal static string Shorten(string title) =>
        title.Length <= 11 ? title : title[..10].TrimEnd() + "…";
}
