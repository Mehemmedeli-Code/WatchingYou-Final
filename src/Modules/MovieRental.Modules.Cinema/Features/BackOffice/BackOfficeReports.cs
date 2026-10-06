using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Documents;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// What the manager reads: today at a glance, sales over a range, and what is owed to each
// distributor. Sales are dated by when the money was taken; the settlement is dated by when
// the film played, because that is how distributors invoice.

// ------------------------------------------------------------------ dashboard

public sealed record DayRevenue(string Day, decimal BoxOffice, decimal Bar);

public sealed record OpenShiftInfo(Guid Id, string CashierName, DateTime OpenedAtUtc, decimal SalesTotal);

public sealed record TodayScreening(
    Guid Id, DateTime StartsAtUtc, string MovieTitle, string VenueName, string Hall, int Sold, int Capacity);

public sealed record BackOfficeDashboard(
    string Day,
    decimal BoxOffice, decimal BoxOfficeOnline, decimal BoxOfficeCounter, int Tickets,
    decimal Bar, int BarReceipts, decimal BarMargin, decimal SpendPerHead,
    int ScreeningsToday, double OccupancyToday,
    IReadOnlyList<OpenShiftInfo> OpenShifts,
    IReadOnlyList<ConcessionItemDto> LowStock,
    IReadOnlyList<TodayScreening> Screenings,
    IReadOnlyList<DayRevenue> LastSevenDays);

public sealed record GetBackOfficeDashboardQuery : IQuery<BackOfficeDashboard>;

internal sealed class GetBackOfficeDashboardHandler(CinemaDbContext db)
    : IQueryHandler<GetBackOfficeDashboardQuery, BackOfficeDashboard>
{
    public async Task<BackOfficeDashboard> Handle(GetBackOfficeDashboardQuery query, CancellationToken ct)
    {
        var today = BackOfficeClock.Today;
        var weekStart = today.AddDays(-6);
        var fromUtc = BackOfficeClock.StartUtc(weekStart);
        var todayUtc = BackOfficeClock.StartUtc(today);
        var tomorrowUtc = BackOfficeClock.StartUtc(today.AddDays(1));

        var payments = await SalesData.PaymentsAsync(db, fromUtc, tomorrowUtc, ct);
        var bar = await SalesData.BarAsync(db, fromUtc, tomorrowUtc, ct);

        var todayPayments = payments.Where(p => p.AtUtc >= todayUtc).ToList();
        var todayBar = bar.Where(b => b.AtUtc >= todayUtc).ToList();
        var tickets = todayPayments.Sum(p => p.Seats);
        var barTotal = todayBar.Sum(b => b.Total);

        var screenings = await db.Screenings.AsNoTracking()
            .Where(s => s.StartsAtUtc >= todayUtc && s.StartsAtUtc < tomorrowUtc && !s.IsCancelled)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new TodayScreening(
                s.Id, s.StartsAtUtc, s.MovieTitle, s.HallRoom!.Venue!.Name, s.Hall,
                s.Bookings.Count(b => b.ConfirmedAtUtc != null), s.Rows * s.SeatsPerRow))
            .ToListAsync(ct);

        var capacity = screenings.Sum(s => s.Capacity);
        var sold = screenings.Sum(s => s.Sold);

        var openShifts = await db.CashShifts.AsNoTracking().Where(s => s.ClosedAtUtc == null)
            .OrderBy(s => s.CreatedAtUtc).ToListAsync(ct);
        var shiftTotals = await ShiftTotals.ForAsync(db, openShifts, ct);

        var lowStock = (await db.ConcessionItems.AsNoTracking()
                .Where(i => i.IsActive && i.Stock <= i.LowStockThreshold)
                .OrderBy(i => i.Stock).ToListAsync(ct))
            .Select(i => i.ToDto()).ToList();

        var week = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).Select(day => new DayRevenue(
            day.ToString("dd.MM", CultureInfo.InvariantCulture),
            payments.Where(p => p.Day == day).Sum(p => p.Net),
            bar.Where(b => b.Day == day).Sum(b => b.Total))).ToList();

        return new BackOfficeDashboard(
            today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            todayPayments.Sum(p => p.Net),
            todayPayments.Where(p => p.Provider != PaymentProvider.BoxOffice).Sum(p => p.Net),
            todayPayments.Where(p => p.Provider == PaymentProvider.BoxOffice).Sum(p => p.Net),
            tickets,
            barTotal, todayBar.Count, barTotal - todayBar.Sum(b => b.Cost),
            // Spend per head — the bar's revenue divided by admissions. The number cinema
            // operators watch most closely after attendance itself.
            tickets == 0 ? 0 : Math.Round(barTotal / tickets, 2),
            screenings.Count, capacity == 0 ? 0 : Math.Round(sold * 100.0 / capacity, 1),
            [.. shiftTotals.Select(s => new OpenShiftInfo(s.Id, s.CashierName, s.OpenedAtUtc, s.SalesTotal))],
            lowStock, screenings, week);
    }
}

// ------------------------------------------------------------------ sales report

public sealed record SalesTotals(
    decimal BoxOffice, decimal Online, decimal Counter, decimal Refunded, int Tickets,
    decimal Bar, decimal BarCost, decimal BarMargin, int BarReceipts, decimal SpendPerHead, decimal Total);

public sealed record FilmSales(string MovieTitle, int Tickets, decimal Gross);

public sealed record HallSales(string VenueName, string Hall, int Screenings, int Tickets, int Capacity, double Occupancy, decimal Gross);

public sealed record TicketTypeSales(string TicketType, int Tickets, decimal Revenue);

public sealed record BarItemSales(string Name, int Quantity, decimal Revenue, decimal Margin);

public sealed record SalesReport(
    string From, string To, SalesTotals Totals, IReadOnlyList<DayRevenue> Daily,
    IReadOnlyList<FilmSales> ByFilm, IReadOnlyList<HallSales> ByHall,
    IReadOnlyList<TicketTypeSales> ByTicketType, IReadOnlyList<BarItemSales> TopBar);

public sealed record GetSalesReportQuery(string? From, string? To) : IQuery<SalesReport>;

internal sealed class GetSalesReportHandler(CinemaDbContext db) : IQueryHandler<GetSalesReportQuery, SalesReport>
{
    public async Task<SalesReport> Handle(GetSalesReportQuery query, CancellationToken ct)
    {
        var (from, to, fromUtc, toUtc) = BackOfficeClock.Range(query.From, query.To);

        var payments = await SalesData.PaymentsAsync(db, fromUtc, toUtc, ct);
        var bar = await SalesData.BarAsync(db, fromUtc, toUtc, ct);

        var tickets = payments.Sum(p => p.Seats);
        var barTotal = bar.Sum(b => b.Total);
        var barCost = bar.Sum(b => b.Cost);
        var boxOffice = payments.Sum(p => p.Net);

        var totals = new SalesTotals(
            boxOffice,
            payments.Where(p => p.Provider != PaymentProvider.BoxOffice).Sum(p => p.Net),
            payments.Where(p => p.Provider == PaymentProvider.BoxOffice).Sum(p => p.Net),
            payments.Sum(p => p.Refunded), tickets,
            barTotal, barCost, barTotal - barCost, bar.Count,
            tickets == 0 ? 0 : Math.Round(barTotal / tickets, 2),
            boxOffice + barTotal);

        var daily = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i))
            .Select(day => new DayRevenue(day.ToString("dd.MM", CultureInfo.InvariantCulture),
                payments.Where(p => p.Day == day).Sum(p => p.Net), bar.Where(b => b.Day == day).Sum(b => b.Total)))
            .ToList();

        var byFilm = payments.GroupBy(p => p.MovieTitle)
            .Select(g => new FilmSales(g.Key, g.Sum(p => p.Seats), g.Sum(p => p.Net)))
            .OrderByDescending(f => f.Gross).ToList();

        // Occupancy is about performances in the range, so it is read by start time.
        var halls = await db.Screenings.AsNoTracking()
            .Where(s => s.StartsAtUtc >= fromUtc && s.StartsAtUtc < toUtc && !s.IsCancelled)
            .Select(s => new
            {
                s.HallId, Venue = s.HallRoom!.Venue!.Name, s.Hall, Capacity = s.Rows * s.SeatsPerRow,
                Sold = s.Bookings.Count(b => b.ConfirmedAtUtc != null)
            })
            .ToListAsync(ct);

        var grossByHall = payments.GroupBy(p => p.HallId).ToDictionary(g => g.Key, g => g.Sum(p => p.Net));
        var byHall = halls.GroupBy(h => new { h.HallId, h.Venue, h.Hall })
            .Select(g =>
            {
                var capacity = g.Sum(x => x.Capacity);
                var sold = g.Sum(x => x.Sold);
                return new HallSales(g.Key.Venue, g.Key.Hall, g.Count(), sold, capacity,
                    capacity == 0 ? 0 : Math.Round(sold * 100.0 / capacity, 1), grossByHall.GetValueOrDefault(g.Key.HallId));
            })
            .OrderBy(h => h.VenueName).ThenBy(h => h.Hall).ToList();

        var seats = await db.SeatBookings.AsNoTracking()
            .Where(b => b.ConfirmedAtUtc >= fromUtc && b.ConfirmedAtUtc < toUtc && b.Payment!.Status == PaymentStatus.Confirmed)
            .Select(b => new { b.TicketType, b.PricePaid })
            .ToListAsync(ct);
        var byType = seats.GroupBy(s => s.TicketType ?? "Online")
            .Select(g => new TicketTypeSales(g.Key, g.Count(), g.Sum(s => s.PricePaid)))
            .OrderByDescending(t => t.Tickets).ToList();

        var lines = await db.ConcessionSaleLines.AsNoTracking()
            .Where(l => l.CreatedAtUtc >= fromUtc && l.CreatedAtUtc < toUtc)
            .Select(l => new { l.Name, l.Quantity, l.UnitPrice, l.UnitCost })
            .ToListAsync(ct);
        var topBar = lines.GroupBy(l => l.Name)
            .Select(g => new BarItemSales(g.Key, g.Sum(l => l.Quantity),
                g.Sum(l => l.UnitPrice * l.Quantity), g.Sum(l => (l.UnitPrice - l.UnitCost) * l.Quantity)))
            .OrderByDescending(x => x.Revenue).Take(15).ToList();

        return new SalesReport(
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            totals, daily, byFilm, byHall, byType, topBar);
    }
}

// ------------------------------------------------------------------ distributor settlement

public sealed record SettlementRow(
    Guid MovieId, string MovieTitle, string? Distributor, decimal SharePercent, bool HasDeal,
    int Screenings, int Tickets, decimal Gross, decimal DistributorDue, decimal CinemaNet);

public sealed record SettlementReport(
    string From, string To, IReadOnlyList<SettlementRow> Rows,
    decimal Gross, decimal DistributorDue, decimal CinemaNet);

public sealed record GetSettlementQuery(string? From, string? To) : IQuery<SettlementReport>;

internal sealed class GetSettlementHandler(CinemaDbContext db) : IQueryHandler<GetSettlementQuery, SettlementReport>
{
    public async Task<SettlementReport> Handle(GetSettlementQuery query, CancellationToken ct)
    {
        var (from, to, fromUtc, toUtc) = BackOfficeClock.Range(query.From, query.To);

        var played = await db.Screenings.AsNoTracking()
            .Where(s => s.StartsAtUtc >= fromUtc && s.StartsAtUtc < toUtc && !s.IsCancelled)
            .Select(s => new { s.Id, s.MovieId, s.MovieTitle })
            .ToListAsync(ct);
        var ids = played.Select(s => s.Id).ToList();

        var money = await db.SeatPayments.AsNoTracking()
            .Where(p => ids.Contains(p.ScreeningId)
                     && (p.Status == PaymentStatus.Confirmed || p.Status == PaymentStatus.Refunded))
            .Select(p => new { p.ScreeningId, Net = p.Amount - (p.RefundedAmount ?? 0m), Seats = p.Seats.Count })
            .ToListAsync(ct);

        var deals = await db.FilmDeals.AsNoTracking().ToDictionaryAsync(d => d.MovieId, ct);

        var rows = played.GroupBy(s => s.MovieId).Select(g =>
        {
            var screeningIds = g.Select(s => s.Id).ToHashSet();
            var mine = money.Where(m => screeningIds.Contains(m.ScreeningId)).ToList();
            var gross = mine.Sum(m => m.Net);
            deals.TryGetValue(g.Key, out var deal);
            var share = deal?.SharePercent ?? 0m;
            var due = Math.Round(gross * share / 100m, 2);
            return new SettlementRow(g.Key, deal?.MovieTitle ?? g.First().MovieTitle, deal?.Distributor, share, deal is not null,
                g.Count(), mine.Sum(m => m.Seats), gross, due, gross - due);
        })
        .OrderByDescending(r => r.Gross).ToList();

        return new SettlementReport(
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rows, rows.Sum(r => r.Gross), rows.Sum(r => r.DistributorDue), rows.Sum(r => r.CinemaNet));
    }
}

// ------------------------------------------------------------------ shared reads

internal static class SalesData
{
    public sealed record Payment(
        DateTime AtUtc, DateOnly Day, decimal Net, decimal Refunded, PaymentProvider Provider,
        int Seats, string MovieTitle, Guid HallId);

    public sealed record Bar(DateTime AtUtc, DateOnly Day, decimal Total, decimal Cost);

    /// <summary>Money taken in the window, after refunds, dated by confirmation.</summary>
    public static async Task<List<Payment>> PaymentsAsync(CinemaDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = await db.SeatPayments.AsNoTracking()
            .Where(p => p.ConfirmedAtUtc >= fromUtc && p.ConfirmedAtUtc < toUtc
                     && (p.Status == PaymentStatus.Confirmed || p.Status == PaymentStatus.Refunded))
            .Select(p => new
            {
                At = p.ConfirmedAtUtc!.Value, p.Amount, Refunded = p.RefundedAmount ?? 0m, p.Provider,
                Seats = p.Seats.Count, p.Screening!.MovieTitle, p.Screening.HallId
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new Payment(r.At, BackOfficeClock.LocalDay(r.At), r.Amount - r.Refunded, r.Refunded,
            r.Provider, r.Seats, r.MovieTitle, r.HallId))];
    }

    public static async Task<List<Bar>> BarAsync(CinemaDbContext db, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        [.. (await db.ConcessionSales.AsNoTracking()
                .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
                .Select(s => new { s.CreatedAtUtc, s.Total, s.Cost })
                .ToListAsync(ct))
            .Select(s => new Bar(s.CreatedAtUtc, BackOfficeClock.LocalDay(s.CreatedAtUtc), s.Total, s.Cost))];
}

public static class BackOfficeReportEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/backoffice").WithTags("Back office").RequireAuthorization(AppRoles.Admin);

        reports.MapGet("/dashboard", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetBackOfficeDashboardQuery(), ct)))
            .WithName("GetBackOfficeDashboard");

        reports.MapGet("/reports/sales", async (string? from, string? to, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetSalesReportQuery(from, to), ct)))
            .WithName("GetSalesReport");

        reports.MapGet("/reports/settlement", async (string? from, string? to, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetSettlementQuery(from, to), ct)))
            .WithName("GetSettlementReport");

        reports.MapGet("/reports/settlement.csv", async (string? from, string? to, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var report = await dispatcher.Ask(new GetSettlementQuery(from, to), ct);
                var csv = Csv.Build(
                    ["Film", "Distributor", "Share %", "Screenings", "Tickets", "Gross", "Distributor due", "Cinema net"],
                    report.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
                    {
                        r.MovieTitle, r.Distributor ?? "", r.SharePercent, r.Screenings, r.Tickets,
                        r.Gross, r.DistributorDue, r.CinemaNet
                    }));
                return Results.File(csv, Csv.ContentType, $"settlement-{report.From}-{report.To}.csv");
            })
            .WithName("ExportSettlement");
    }
}
