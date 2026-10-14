using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Infrastructure;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Documents;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// Admin tools that save typing: a week of screenings in one go, and the bookings as a CSV.

// ------------------------------------------------------------------ bulk scheduling

/// <param name="FirstDate">Local (Baku) date of the first day, yyyy-MM-dd.</param>
/// <param name="Days">How many days from FirstDate to cover.</param>
/// <param name="Times">Local start times, HH:mm.</param>
/// <param name="Weekdays">0 = Sunday … 6 = Saturday. Empty means every day.</param>
public sealed record BulkScheduleCommand(
    Guid MovieId, Guid HallId, string FirstDate, int Days, IReadOnlyList<string> Times,
    IReadOnlyList<int>? Weekdays, decimal SeatPrice, string AudioLanguage, string? SubtitleLanguage)
    : ICommand<Result<BulkScheduleResult>>;

public sealed record BulkScheduleResult(int Created, IReadOnlyList<string> Skipped);

internal sealed class BulkScheduleValidator : AbstractValidator<BulkScheduleCommand>
{
    private static readonly string[] Languages = ["az", "en", "ru", "tr"];

    public BulkScheduleValidator()
    {
        RuleFor(x => x.MovieId).NotEmpty();
        RuleFor(x => x.HallId).NotEmpty();
        RuleFor(x => x.FirstDate).Must(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .WithMessage("First day must be a date (yyyy-MM-dd).");
        RuleFor(x => x.Days).InclusiveBetween(1, 28);
        RuleFor(x => x.Times).NotEmpty().WithMessage("Add at least one start time.");
        // Guarded: a body without "times" is a 400 from NotEmpty, not a crash in this rule.
        RuleFor(x => x.Times.Count).LessThanOrEqualTo(8).When(x => x.Times is not null);
        RuleForEach(x => x.Times).Must(t => TimeOnly.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .WithMessage("Times are written HH:mm, e.g. 19:30.");
        RuleFor(x => x.SeatPrice).GreaterThan(0).LessThan(1000);
        RuleFor(x => x.AudioLanguage).Must(l => Languages.Contains(l)).WithMessage("Audio language must be one of az, en, ru, tr.");
        RuleFor(x => x.SubtitleLanguage).Must(l => l is null || Languages.Contains(l)).WithMessage("Subtitle language must be one of az, en, ru, tr.");
        RuleForEach(x => x.Weekdays).InclusiveBetween(0, 6).WithMessage("Weekdays run from 0 (Sunday) to 6 (Saturday).");
    }
}

internal sealed class BulkScheduleHandler(CinemaDbContext db, ICatalogApi catalog, IAuditLog audit)
    : ICommandHandler<BulkScheduleCommand, Result<BulkScheduleResult>>
{
    public const int MaxScreenings = 120;

    /// <summary>Two screenings in one hall need this much between their starts: a feature
    /// plus the time to clean the room and let the next audience in.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(150);

    private static readonly TimeSpan Baku = TimeSpan.FromHours(4);

    public async Task<Result<BulkScheduleResult>> Handle(BulkScheduleCommand command, CancellationToken ct)
    {
        var hall = await db.Halls.Include(h => h.Venue).FirstOrDefaultAsync(h => h.Id == command.HallId, ct);
        if (hall is null) return Result.Failure<BulkScheduleResult>(Error.NotFound("Hall"));

        var movie = await catalog.GetMovieAsync(command.MovieId, ct);
        if (movie is null) return Result.Failure<BulkScheduleResult>(Error.NotFound("Film"));

        var slots = Slots(command).ToList();
        if (slots.Count > MaxScreenings)
            return Result.Failure<BulkScheduleResult>(Error.Validation(
                $"That would create {slots.Count} screenings; {MaxScreenings} is the most in one go."));

        var now = DateTime.UtcNow;
        var first = slots.Count == 0 ? now : slots.Min();
        var last = slots.Count == 0 ? now : slots.Max();

        // Everything already booked into this room over the whole range, read once.
        var taken = await db.Screenings.AsNoTracking()
            .Where(s => s.HallId == hall.Id && !s.IsCancelled
                     && s.StartsAtUtc > first - MinimumGap && s.StartsAtUtc < last + MinimumGap)
            .Select(s => s.StartsAtUtc)
            .ToListAsync(ct);

        var skipped = new List<string>();
        var created = 0;

        foreach (var startUtc in slots)
        {
            var label = (startUtc + Baku).ToString("ddd dd.MM HH:mm", CultureInfo.InvariantCulture);

            if (startUtc <= now) { skipped.Add($"{label} — already in the past"); continue; }
            if (taken.Any(t => (t - startUtc).Duration() < MinimumGap))
            {
                skipped.Add($"{label} — {hall.Name} is busy then");
                continue;
            }

            db.Screenings.Add(new Screening
            {
                MovieId = movie.Id, MovieTitle = movie.Title,
                HallId = hall.Id, Hall = hall.Name,
                StartsAtUtc = startUtc,
                Rows = hall.Rows, SeatsPerRow = hall.SeatsPerRow,
                SeatPrice = command.SeatPrice,
                AudioLanguage = command.AudioLanguage,
                SubtitleLanguage = command.SubtitleLanguage
            });
            taken.Add(startUtc);      // later slots in this same batch must respect it too
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEntry("screening.bulk",
                $"{movie.Title} · {hall.Venue?.Name} {hall.Name}",
                $"{created} screening(s) from {command.FirstDate} over {command.Days} day(s)"), ct);
        }

        return Result.Success(new BulkScheduleResult(created, skipped));
    }

    /// <summary>Every requested local start time, converted to UTC. Exposed for the tests.</summary>
    internal static IEnumerable<DateTime> Slots(BulkScheduleCommand command)
    {
        if (!DateOnly.TryParseExact(command.FirstDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstDay))
            yield break;

        var weekdays = command.Weekdays is { Count: > 0 } w ? w.ToHashSet() : null;
        var times = command.Times
            .Select(t => TimeOnly.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? (TimeOnly?)time : null)
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        for (var i = 0; i < command.Days; i++)
        {
            var day = firstDay.AddDays(i);
            if (weekdays is not null && !weekdays.Contains((int)day.DayOfWeek)) continue;

            foreach (var time in times)
                yield return DateTime.SpecifyKind(day.ToDateTime(time) - Baku, DateTimeKind.Utc);
        }
    }
}

// ------------------------------------------------------------------ CSV export

/// <param name="Pdf">A printable report instead of the spreadsheet.</param>
public sealed record ExportBookingsQuery(DateTime? FromUtc, DateTime? ToUtc, bool Pdf = false) : IQuery<byte[]>;

internal sealed class ExportBookingsHandler(CinemaDbContext db, IUserDirectory users)
    : IQueryHandler<ExportBookingsQuery, byte[]>
{
    public const int MaxRows = 5000;

    public async Task<byte[]> Handle(ExportBookingsQuery query, CancellationToken ct)
    {
        var from = query.FromUtc ?? DateTime.UtcNow.AddDays(-90);
        var to = query.ToUtc ?? DateTime.UtcNow.AddDays(1);

        var payments = await db.SeatPayments.AsNoTracking()
            .Include(p => p.Seats)
            .Include(p => p.Screening)
            .Where(p => p.CreatedAtUtc >= from && p.CreatedAtUtc < to
                     && (p.Status == PaymentStatus.Confirmed || p.Status == PaymentStatus.Refunded))
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(MaxRows)
            .ToListAsync(ct);

        // One directory lookup for every customer at once, not one query per row.
        var contacts = await users.GetContactsAsync([.. payments.Select(p => p.UserId).Distinct()], ct);
        string Email(Guid id) => contacts.GetValueOrDefault(id)?.Email ?? "";

        // Times in Baku, where the cinema is; money in the order a person reads it, ending in what
        // the order actually brought in.
        DateTime? Local(DateTime? utc) => utc is { } u ? SalesReceiptPdf.Baku(u) : null;
        if (query.Pdf) return BookingsPdf(payments, Email, from, to);
        var rows = payments.Select(p => (IReadOnlyList<object?>)new object?[]
        {
            p.Reference, Local(p.ConfirmedAtUtc ?? p.CreatedAtUtc), p.Status == PaymentStatus.Refunded ? "Refunded" : "Paid",
            p.Provider == PaymentProvider.BoxOffice ? $"Box office{(p.Tender is { } t ? $" ({t})" : "")}" : p.Provider == PaymentProvider.Stripe ? "Stripe" : "Online card",
            Email(p.UserId), p.Screening?.MovieTitle, p.Screening?.Hall, Local(p.Screening?.StartsAtUtc),
            string.Join(" ", p.Seats.OrderBy(s => s.Row).ThenBy(s => s.Number).Select(s => $"{(char)('A' + s.Row - 1)}{s.Number}")),
            p.Seats.Count, p.Subtotal, p.PromoCode, p.PromoDiscount, p.PointsRedeemed, p.PointsDiscount,
            p.Amount, p.RefundedAmount ?? 0m, p.Amount - (p.RefundedAmount ?? 0m), p.PointsEarned
        });

        return Csv.Build(
            ["Order", "Date (Baku)", "Status", "Payment method", "Customer", "Film", "Hall", "Screening (Baku)",
             "Seats", "Tickets", "Ticket value AZN", "Promo code", "Promo discount AZN", "Points used", "Points discount AZN",
             "Paid AZN", "Refunded AZN", "Net AZN", "Points earned"],
            rows);
    }

    /// <summary>The same orders on paper: the columns a person reads, totals at the foot.</summary>
    private static byte[] BookingsPdf(List<SeatPayment> payments, Func<Guid, string> email, DateTime fromUtc, DateTime toUtc)
    {
        static string Day(DateTime utc) => SalesReceiptPdf.Baku(utc).ToString("dd.MM.yy HH:mm", CultureInfo.InvariantCulture);
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        var refunded = Lang.PickLatin("Qaytarılıb", "Refunded", "Iade");
        var rows = payments.Select(p => (IReadOnlyList<string>)
        [
            p.Reference, Day(p.ConfirmedAtUtc ?? p.CreatedAtUtc),
            p.Status == PaymentStatus.Refunded ? refunded : Lang.PickLatin("Ödənilib", "Paid", "Ödendi"),
            p.Provider == PaymentProvider.BoxOffice ? Lang.PickLatin("Kassa", "Box office", "Gişe") + (p.Tender is { } t ? $" ({t})" : "")
                : p.Provider == PaymentProvider.Stripe ? "Stripe" : Lang.PickLatin("Onlayn kart", "Online card", "Online kart"),
            email(p.UserId), p.Screening?.MovieTitle ?? "", p.Screening is { } s ? Day(s.StartsAtUtc) : "",
            string.Join(" ", p.Seats.OrderBy(x => x.Row).ThenBy(x => x.Number).Select(x => $"{(char)('A' + x.Row - 1)}{x.Number}")),
            Money(p.Amount), Money(p.RefundedAmount ?? 0m), Money(p.Amount - (p.RefundedAmount ?? 0m)),
        ]).ToList();
        var paid = payments.Sum(p => p.Amount);
        var back = payments.Sum(p => p.RefundedAmount ?? 0m);
        return TablePdf.Render(
            Lang.PickLatin("Bilet satışları", "Ticket sales", "Bilet satışları"),
            $"{SalesReceiptPdf.Baku(fromUtc):dd.MM.yyyy} - {SalesReceiptPdf.Baku(toUtc):dd.MM.yyyy} · {payments.Count} " +
                Lang.PickLatin("sifariş", "orders", "sipariş") + " · " + Lang.PickLatin("vaxt: Bakı", "times: Baku", "saat: Bakü"),
            [
                new(Lang.PickLatin("Sifariş", "Order", "Sipariş"), 12), new(Lang.PickLatin("Tarix", "Date", "Tarih"), 14),
                new(Lang.PickLatin("Status", "Status", "Durum"), 10), new(Lang.PickLatin("Ödəniş", "Method", "Ödeme"), 18),
                new(Lang.PickLatin("Müştəri", "Customer", "Müşteri"), 28), new("Film", 26),
                new(Lang.PickLatin("Seans", "Screening", "Seans"), 14), new(Lang.PickLatin("Yerlər", "Seats", "Koltuklar"), 14),
                new(Lang.PickLatin("Ödənib", "Paid", "Ödenen"), 9, true), new(Lang.PickLatin("Qaytarılıb", "Refund", "Iade"), 9, true),
                new(Lang.PickLatin("Xalis", "Net", "Net"), 9, true),
            ],
            rows,
            [Lang.PickLatin("CƏMİ", "TOTAL", "TOPLAM"), "", "", "", "", "", "", "", Money(paid), Money(back), Money(paid - back)],
            Lang.PickLatin("Səhifə", "Page", "Sayfa"));
    }
}

// ------------------------------------------------------------------ sales receipt (PDF)

/// <param name="From">First day, Baku time, yyyy-MM-dd; today when left out.</param>
/// <param name="To">Last day, inclusive; the first day when left out.</param>
public sealed record SalesReceiptQuery(string? From, string? To) : IQuery<Result<byte[]>>;

internal sealed class SalesReceiptHandler(CinemaDbContext db) : IQueryHandler<SalesReceiptQuery, Result<byte[]>>
{
    public async Task<Result<byte[]>> Handle(SalesReceiptQuery query, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(SalesReceiptPdf.Baku(DateTime.UtcNow));
        if (!TryDay(query.From, today, out var from) || !TryDay(query.To, from, out var to) || to < from || to.DayNumber - from.DayNumber > 92)
            return Result.Failure<byte[]>(Error.Validation("Pick a period of up to three months (yyyy-MM-dd)."));

        // A sale belongs to the day it was paid, in Baku.
        var fromUtc = from.ToDateTime(TimeOnly.MinValue).AddHours(-4);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-4);
        var payments = await db.SeatPayments.AsNoTracking()
            .Include(p => p.Seats)
            .Include(p => p.Screening)
            .Where(p => (p.Status == PaymentStatus.Confirmed || p.Status == PaymentStatus.Refunded)
                     && (p.ConfirmedAtUtc ?? p.CreatedAtUtc) >= fromUtc && (p.ConfirmedAtUtc ?? p.CreatedAtUtc) < toUtc)
            .Take(ExportBookingsHandler.MaxRows)
            .ToListAsync(ct);

        return Result.Success(SalesReceiptPdf.Render(payments, from, to, DateTime.UtcNow));
    }

    private static bool TryDay(string? value, DateOnly fallback, out DateOnly day)
    {
        day = fallback;
        return string.IsNullOrWhiteSpace(value)
            || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }
}

public static class CinemaAdminToolEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Cinema admin").RequireAuthorization(AppRoles.Admin);

        admin.MapPost("/screenings/bulk", async Task<Results<Ok<BulkScheduleResult>, NotFound<Error>, BadRequest<Error>>> (
                BulkScheduleCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.BadRequest(result.Error);
            })
            .WithName("BulkScheduleScreenings")
            .WithSummary("Schedule a film for many days at once")
            .WithDescription("From `firstDate` (yyyy-MM-dd) for `days` (1-28), at each of `times` (HH:mm, up to 8), optionally only on " +
                             "`weekdays` (0 Sunday … 6 Saturday). Shows that would clash with another in the hall are skipped and listed.");

        admin.MapGet("/export/bookings.csv", async (DateTime? from, DateTime? to, IDispatcher dispatcher, CancellationToken ct) =>
                from > to ? Results.BadRequest(BackwardsPeriod)
                : Results.File(await dispatcher.Ask(new ExportBookingsQuery(from, to), ct), Csv.ContentType,
                    $"watchingyou-bookings-{DateTime.UtcNow:yyyyMMdd}.csv"))
            .WithName("ExportBookings")
            .WithSummary("Download bookings as a spreadsheet (CSV)")
            .WithDescription(PeriodHelp)
            .Produces(StatusCodes.Status200OK, typeof(Stream), Csv.ContentType)
            .Produces<Error>(StatusCodes.Status400BadRequest);

        admin.MapGet("/export/bookings.pdf", async (DateTime? from, DateTime? to, IDispatcher dispatcher, CancellationToken ct) =>
                from > to ? Results.BadRequest(BackwardsPeriod)
                : Results.File(await dispatcher.Ask(new ExportBookingsQuery(from, to, Pdf: true), ct), "application/pdf",
                    $"watchingyou-bookings-{DateTime.UtcNow:yyyyMMdd}.pdf"))
            .WithName("ExportBookingsPdf")
            .WithSummary("Download bookings as a PDF report")
            .WithDescription(PeriodHelp)
            .Produces(StatusCodes.Status200OK, typeof(Stream), "application/pdf")
            .Produces<Error>(StatusCodes.Status400BadRequest);

        // The till prints it too, at the end of the day: admins and cashiers.
        app.MapGet("/api/backoffice/reports/sales-receipt.pdf", async Task<IResult> (string? from, string? to, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Ask(new SalesReceiptQuery(from, to), ct);
                return result.IsSuccess
                    ? Results.File(result.Value, "application/pdf", $"watchingyou-sales-{from ?? DateTime.UtcNow.AddHours(4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf")
                    : Results.BadRequest(result.Error);
            })
            .WithTags("Back office").WithName("SalesReceipt").RequireAuthorization(AppPolicies.BackOffice)
            .WithSummary("The sales receipt for a period, as a PDF")
            .WithDescription("`from` and `to` as yyyy-MM-dd, up to three months; left out, today. 400: not dates, or too long a period.")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "application/pdf")
            .Produces<Error>(StatusCodes.Status400BadRequest);
    }

    private static readonly Error BackwardsPeriod = Error.Validation("`from` is after `to`.");
    private const string PeriodHelp = "Bookings made between `from` and `to` (dates, e.g. 2026-10-01). Left out: the last 90 days up to tomorrow.";
}
