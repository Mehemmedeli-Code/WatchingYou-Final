using System.Globalization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
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
        RuleFor(x => x.Times.Count).LessThanOrEqualTo(8);
        RuleForEach(x => x.Times).Must(t => TimeOnly.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .WithMessage("Times are written HH:mm, e.g. 19:30.");
        RuleFor(x => x.SeatPrice).GreaterThan(0);
        RuleFor(x => x.AudioLanguage).Must(l => Languages.Contains(l));
        RuleFor(x => x.SubtitleLanguage).Must(l => l is null || Languages.Contains(l));
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

public sealed record ExportBookingsQuery(DateTime? FromUtc, DateTime? ToUtc) : IQuery<byte[]>;

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

        // One directory lookup per customer, not per row.
        var emails = new Dictionary<Guid, string>();
        foreach (var userId in payments.Select(p => p.UserId).Distinct())
            emails[userId] = (await users.GetContactAsync(userId, ct))?.Email ?? "";

        var rows = payments.Select(p => (IReadOnlyList<object?>)new object?[]
        {
            p.Reference, p.Status.ToString(), p.ConfirmedAtUtc, emails.GetValueOrDefault(p.UserId),
            p.Screening?.MovieTitle, p.Screening?.Hall, p.Screening?.StartsAtUtc,
            string.Join(" ", p.Seats.OrderBy(s => s.Row).ThenBy(s => s.Number).Select(s => $"{(char)('A' + s.Row - 1)}{s.Number}")),
            p.Seats.Count, p.Subtotal, p.PromoCode, p.PromoDiscount, p.PointsRedeemed, p.PointsDiscount,
            p.Amount, p.RefundedAmount, p.Provider.ToString(), p.PointsEarned
        });

        return Csv.Build(
            ["Reference", "Status", "Confirmed (UTC)", "Customer", "Film", "Hall", "Starts (UTC)",
             "Seats", "Seat count", "Subtotal", "Promo code", "Promo discount", "Points used", "Points discount",
             "Paid", "Refunded", "Provider", "Points earned"],
            rows);
    }
}

public static class CinemaAdminToolEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Cinema").RequireAuthorization(AppRoles.Admin);

        admin.MapPost("/screenings/bulk", async Task<Results<Ok<BulkScheduleResult>, NotFound<Error>, BadRequest<Error>>> (
                BulkScheduleCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.BadRequest(result.Error);
            })
            .WithName("BulkScheduleScreenings");

        admin.MapGet("/export/bookings.csv", async (DateTime? from, DateTime? to, IDispatcher dispatcher, CancellationToken ct) =>
                Results.File(await dispatcher.Ask(new ExportBookingsQuery(from, to), ct), Csv.ContentType,
                    $"watchingyou-bookings-{DateTime.UtcNow:yyyyMMdd}.csv"))
            .WithName("ExportBookings");
    }
}
