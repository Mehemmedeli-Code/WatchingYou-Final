using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// The door. QR codes were only half a feature while nothing read them back.
//
// The scanner sends whatever the code contained, or a reference typed by hand. The answer is
// one of five states, and the wrong ones matter more than the right one: a doorman needs to
// know *why* a ticket is being refused, not just that it is.

public enum CheckInOutcome
{
    Admitted = 1,
    AlreadyUsed = 2,
    WrongScreening = 3,
    NotConfirmed = 4,
    NotFound = 5
}

public sealed record CheckInResult(
    CheckInOutcome Outcome, string? Reference, string? MovieTitle, string? Hall,
    string? VenueName, DateTime? StartsAtUtc, string? Seat, DateTime? UsedAtUtc, string Message);

/// <param name="Payload">Either the full QR payload or a bare reference. Seat is optional:
/// scanning one seat's code admits that seat, while a typed reference admits the next
/// unused seat on the booking.</param>
public sealed record CheckInCommand(string Payload, Guid? ScreeningId) : ICommand<CheckInResult>;

internal sealed class CheckInValidator : AbstractValidator<CheckInCommand>
{
    // An empty scan used to reach Parse(null) and answer 500.
    public CheckInValidator() => RuleFor(x => x.Payload).NotEmpty().WithMessage("Scan a ticket or type its reference.");
}

internal sealed class CheckInHandler(CinemaDbContext db, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<CheckInCommand, CheckInResult>
{
    public async Task<CheckInResult> Handle(CheckInCommand command, CancellationToken ct)
    {
        var (reference, seatLabel) = Parse(command.Payload);

        if (string.IsNullOrWhiteSpace(reference))
            return new CheckInResult(CheckInOutcome.NotFound, null, null, null, null, null, null, null,
                "Nothing readable in that code.");

        var payment = await db.SeatPayments
            .Include(p => p.Seats)
            .Include(p => p.Screening!).ThenInclude(s => s.HallRoom!).ThenInclude(h => h.Venue)
            .FirstOrDefaultAsync(p => p.Reference == reference, ct);

        if (payment is null)
            return new CheckInResult(CheckInOutcome.NotFound, reference, null, null, null, null, null, null,
                "No booking with that reference.");

        var screening = payment.Screening;
        var context = (payment.Reference, screening?.MovieTitle, screening?.Hall,
                       screening?.HallRoom?.Venue?.Name, screening?.StartsAtUtc);

        if (payment.Status != PaymentStatus.Confirmed)
            return new CheckInResult(CheckInOutcome.NotConfirmed, context.Reference, context.MovieTitle,
                context.Hall, context.Item4, context.StartsAtUtc, seatLabel, null,
                "This booking was never confirmed. The seats went back on sale.");

        // Guards against the honest mistake as much as the dishonest one: the right ticket at
        // the wrong door on a busy evening.
        if (command.ScreeningId is { } expected && payment.ScreeningId != expected)
            return new CheckInResult(CheckInOutcome.WrongScreening, context.Reference, context.MovieTitle,
                context.Hall, context.Item4, context.StartsAtUtc, seatLabel, null,
                "Valid ticket, but for a different screening.");

        var seat = seatLabel is null
            ? payment.Seats.Where(s => s.CheckedInAtUtc is null).OrderBy(s => s.Row).ThenBy(s => s.Number).FirstOrDefault()
            : payment.Seats.FirstOrDefault(s => TicketMapper.Label(s.Row, s.Number) == seatLabel);

        if (seat is null)
        {
            var allUsed = payment.Seats.All(s => s.CheckedInAtUtc is not null);
            return new CheckInResult(allUsed ? CheckInOutcome.AlreadyUsed : CheckInOutcome.NotFound,
                context.Reference, context.MovieTitle, context.Hall, context.Item4, context.StartsAtUtc,
                seatLabel, payment.Seats.Max(s => s.CheckedInAtUtc),
                allUsed ? "Every seat on this booking is already checked in." : "That seat is not on this booking.");
        }

        if (seat.CheckedInAtUtc is { } used)
            return new CheckInResult(CheckInOutcome.AlreadyUsed, context.Reference, context.MovieTitle,
                context.Hall, context.Item4, context.StartsAtUtc, TicketMapper.Label(seat.Row, seat.Number), used,
                "This seat has already been used.");

        seat.CheckedInAtUtc = DateTime.UtcNow;
        seat.CheckedInByUserId = currentUser.Id;
        await db.SaveChangesAsync(ct);

        var label = TicketMapper.Label(seat.Row, seat.Number);
        await audit.RecordAsync(new AuditEntry("ticket.checkin",
            $"{payment.Reference} · {label}", context.MovieTitle, payment.Id), ct);

        return new CheckInResult(CheckInOutcome.Admitted, context.Reference, context.MovieTitle,
            context.Hall, context.Item4, context.StartsAtUtc, label, seat.CheckedInAtUtc, "Admitted.");
    }

    /// <summary>Accepts a scanned payload or a reference somebody read off a phone screen.
    /// Doormen type; the parser should not care which happened.</summary>
    private static (string Reference, string? Seat) Parse(string payload)
    {
        var text = payload.Trim();

        if (text.StartsWith("WATCHINGYOU|", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split('|');
            return (parts.ElementAtOrDefault(1)?.Trim().ToUpperInvariant() ?? "",
                    parts.ElementAtOrDefault(3)?.Trim().ToUpperInvariant());
        }

        return (text.ToUpperInvariant(), null);
    }
}

public static class CheckInEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/tickets/check-in",
            async (CheckInCommand body, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Send(body, ct)))
        .WithName("CheckInTicket").WithTags("Cinema")
        .RequireAuthorization(AppPolicies.SecurityDesk);
}
