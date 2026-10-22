using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Rentals.Infrastructure;

internal sealed class RentalAnalytics(RentalsDbContext db, ICatalogApi catalog) : IRentalAnalytics
{
    public async Task<ChartSeries> MostRentedAsync(int take, CancellationToken ct = default)
    {
        // MovieTitle is snapshotted on the rental row, so this needs no cross-module call:
        // a rental should keep reading correctly even if the title is later renamed.
        var rows = await db.Rentals
            .GroupBy(r => r.MovieTitle)
            .Select(g => new { Title = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync(ct);

        return new ChartSeries(
            "most-rented", "Most rented", "rentals",
            "Titles that left the shelf most often.",
            [.. rows.Select(r => new ChartPoint(Shorten(r.Title), r.Count))]);
    }

    public async Task<ChartSeries> RentalsByGenreAsync(int take, CancellationToken ct = default)
    {
        // Genre belongs to the catalogue, so it is asked for through the contract rather
        // than joined across schemas. Grouping first keeps that to one call per title.
        var perMovie = await db.Rentals
            .GroupBy(r => r.MovieId)
            .Select(g => new { MovieId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byGenre = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in perMovie)
        {
            var movie = await catalog.GetMovieAsync(row.MovieId, ct);
            var genre = movie?.Genre ?? "Unknown";
            byGenre[genre] = byGenre.GetValueOrDefault(genre) + row.Count;
        }

        return new ChartSeries(
            "by-genre", "Rentals by genre", "rentals",
            "Which shelves your customers actually walk to.",
            [.. byGenre.OrderByDescending(p => p.Value).Take(take)
                       .Select(p => new ChartPoint(Shorten(p.Key), p.Value))]);
    }

    private static string Shorten(string text) =>
        text.Length <= 11 ? text : text[..10].TrimEnd() + "…";
}
