using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>The cinema's slice of the admin dashboard. Reads its own tables only; the host
/// puts the series next to the catalogue's and the rentals' without joining anything.</summary>
internal sealed class CinemaAnalytics(CinemaDbContext db) : ICinemaAnalytics, ICinemaTotals
{
    /// <summary>Days are cut on Baku time, not UTC: a ticket sold at 01:30 local belongs to
    /// the day the manager thinks it was sold on.</summary>
    private static readonly TimeSpan Baku = TimeSpan.FromHours(4);

    public async Task<ChartSeries> RevenueByDayAsync(int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 31);
        var todayLocal = (DateTime.UtcNow + Baku).Date;
        var fromUtc = todayLocal.AddDays(-(days - 1)) - Baku;

        var rows = await db.SeatPayments.AsNoTracking()
            .Where(p => p.ConfirmedAtUtc != null && p.ConfirmedAtUtc >= fromUtc
                     && (p.Status == PaymentStatus.Confirmed || p.Status == PaymentStatus.Refunded))
            .Select(p => new { At = p.ConfirmedAtUtc!.Value, p.Amount, Refunded = p.RefundedAmount ?? 0m })
            .ToListAsync(ct);

        var byDay = rows
            .GroupBy(r => (r.At + Baku).Date)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount - r.Refunded));

        var points = Enumerable.Range(0, days)
            .Select(i => todayLocal.AddDays(-(days - 1) + i))
            .Select(day => new ChartPoint(
                day.ToString("dd.MM", CultureInfo.InvariantCulture),
                (double)Math.Round(byDay.GetValueOrDefault(day), 1)))
            .ToList();

        return new ChartSeries("cinema-revenue", "Ticket revenue", "AZN per day",
            $"Cinema sales for the last {days} days, after refunds.", points);
    }

    public async Task<ChartSeries> OccupancyAsync(int take, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var screenings = await db.Screenings.AsNoTracking()
            .Where(s => s.StartsAtUtc > now && !s.IsCancelled)
            .OrderBy(s => s.StartsAtUtc)
            .Take(take)
            .Select(s => new
            {
                s.MovieTitle, s.StartsAtUtc, Capacity = s.Rows * s.SeatsPerRow,
                Sold = s.Bookings.Count(b => b.ConfirmedAtUtc != null)
            })
            .ToListAsync(ct);

        return new ChartSeries("occupancy", "How full", "% of seats sold",
            "The next screenings on the schedule.",
            [.. screenings.Select(s => new ChartPoint(
                Shorten(s.MovieTitle),
                s.Capacity == 0 ? 0 : Math.Round(s.Sold * 100.0 / s.Capacity)))]);
    }

    public async Task<ChartSeries> TopFilmsAsync(int take, CancellationToken ct = default)
    {
        // Title first, then group: the plainest shape for EF to turn into one GROUP BY.
        var rows = await db.SeatBookings.AsNoTracking()
            .Where(b => b.ConfirmedAtUtc != null)
            .Select(b => b.Screening!.MovieTitle)
            .GroupBy(title => title)
            .Select(g => new { Title = g.Key, Tickets = g.Count() })
            .OrderByDescending(x => x.Tickets)
            .Take(take)
            .ToListAsync(ct);

        return new ChartSeries("top-films", "Best sellers", "tickets",
            "Films by cinema tickets sold.",
            [.. rows.Select(r => new ChartPoint(Shorten(r.Title), r.Tickets))]);
    }

    public async Task<CinemaTotals> TotalsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var monthStartLocal = new DateTime((now + Baku).Year, (now + Baku).Month, 1);
        var fromUtc = monthStartLocal - Baku;

        var month = await db.SeatPayments.AsNoTracking()
            .Where(p => p.ConfirmedAtUtc >= fromUtc && (p.Status == PaymentStatus.Confirmed || p.Status == PaymentStatus.Refunded))
            .Select(p => new { p.Amount, Refunded = p.RefundedAmount ?? 0m, p.Status, Seats = p.Seats.Count })
            .ToListAsync(ct);

        var upcoming = await db.Screenings.CountAsync(s => s.StartsAtUtc > now && !s.IsCancelled, ct);
        var promos = await db.PromoCodes.CountAsync(p => p.IsActive && (p.ValidUntilUtc == null || p.ValidUntilUtc > now), ct);

        return new CinemaTotals(
            month.Sum(p => p.Amount - p.Refunded),
            month.Where(p => p.Status == PaymentStatus.Confirmed).Sum(p => p.Seats),
            upcoming, promos);
    }

    private static string Shorten(string title) => title.Length <= 11 ? title : title[..10].TrimEnd() + "…";
}
