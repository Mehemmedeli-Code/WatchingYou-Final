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
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// Booking is two steps: pay, then confirm with the code that lands in your inbox.
//
// The seats are written to the database at step one, unconfirmed. That may look eager, but
// it is what makes the hold real — the unique index on (screening, row, number) is the only
// thing that can truthfully stop two people paying for the same seat, and an in-memory
// reservation would not be protected by it.

public sealed record SeatSelection(int Row, int Number);

public sealed record CardDetails(string Number, int ExpiryMonth, int ExpiryYear, string Cvc, string HolderName);

public sealed record CheckoutCommand(
    Guid ScreeningId, IReadOnlyList<SeatSelection> Seats, CardDetails Card) : ICommand<Result<CheckoutStarted>>;

public sealed record CheckoutStarted(
    Guid PaymentId, string Reference, decimal Amount, string Brand, string Last4,
    string MaskedEmail, DateTime ExpiresAtUtc);

public sealed record TicketSeat(int Row, int Number, string Label, string QrPayload);

public sealed record TicketResponse(
    Guid PaymentId, string Reference, string MovieTitle, string Hall, DateTime StartsAtUtc,
    string AudioLanguage, string? SubtitleLanguage,
    IReadOnlyList<TicketSeat> Seats, decimal Amount, string Brand, string Last4,
    DateTime ConfirmedAtUtc);

internal sealed class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.Seats).NotEmpty().WithMessage("Pick at least one seat.");
        RuleFor(x => x.Seats.Count).LessThanOrEqualTo(8).WithMessage("Eight seats is the limit per booking.");
        RuleFor(x => x.Card.HolderName).NotEmpty().MaximumLength(120).WithMessage("Enter the name on the card.");
    }
}

internal sealed class CheckoutHandler(
    CinemaDbContext db, ICurrentUser currentUser, IUserDirectory users, IEmailSender email)
    : ICommandHandler<CheckoutCommand, Result<CheckoutStarted>>
{
    public async Task<Result<CheckoutStarted>> Handle(CheckoutCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        // --- card, before anything is written -------------------------------------------
        var digits = CardValidation.Digits(command.Card.Number);
        if (!CardValidation.PassesLuhn(digits))
            return Result.Failure<CheckoutStarted>(Error.Validation("That card number is not valid."));

        var brand = CardValidation.BrandOf(digits);
        if (brand == CardBrand.Unknown)
            return Result.Failure<CheckoutStarted>(Error.Validation("Only Visa and Mastercard are accepted."));

        if (!CardValidation.ExpiryIsFuture(command.Card.ExpiryMonth, command.Card.ExpiryYear))
            return Result.Failure<CheckoutStarted>(Error.Validation("That expiry date has passed."));

        if (!CardValidation.CvcLooksRight(CardValidation.Digits(command.Card.Cvc)))
            return Result.Failure<CheckoutStarted>(Error.Validation("The security code should be three or four digits."));

        var last4 = digits[^4..];
        // From here on the number and the CVC are never touched again. Nothing below can
        // persist them because nothing below can see them.

        var screening = await db.Screenings.FirstOrDefaultAsync(s => s.Id == command.ScreeningId, ct);
        if (screening is null) return Result.Failure<CheckoutStarted>(Error.NotFound("Screening"));
        if (screening.StartsAtUtc <= DateTime.UtcNow)
            return Result.Failure<CheckoutStarted>(Error.Conflict("This screening has already started."));

        await ReleaseExpiredHoldsAsync(screening.Id, ct);

        foreach (var seat in command.Seats)
        {
            if (seat.Row < 1 || seat.Row > screening.Rows || seat.Number < 1 || seat.Number > screening.SeatsPerRow)
                return Result.Failure<CheckoutStarted>(Error.Validation($"Seat {seat.Row}-{seat.Number} is not in this hall."));
        }

        var contact = await users.GetContactAsync(userId, ct);
        if (contact is null) return Result.Failure<CheckoutStarted>(Error.Validation("Your account has no e-mail address."));

        var code = Random.Shared.Next(0, 1_000_000).ToString("D6");
        var (hash, salt) = CodeHasher.Create(code);

        var payment = new SeatPayment
        {
            ScreeningId = screening.Id,
            UserId = userId,
            Reference = NewReference(),
            Amount = Math.Round(screening.SeatPrice * command.Seats.Count, 2),
            Brand = brand,
            Last4 = last4,
            CardHolder = command.Card.HolderName.Trim(),
            CodeHash = hash,
            Salt = salt,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            Seats = [.. command.Seats.Select(seat => new SeatBooking
            {
                ScreeningId = screening.Id,
                UserId = userId,
                Row = seat.Row,
                Number = seat.Number,
                PricePaid = screening.SeatPrice
            })]
        };

        db.SeatPayments.Add(payment);

        try
        {
            // One SaveChanges for the whole basket: a partly-held row leaves someone paying
            // for seats they cannot sit together in.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<CheckoutStarted>(
                Error.Conflict("One of those seats was taken while you were paying. Reload the map and try again."));
        }

        await email.SendAsync(new EmailRequest(contact.Email,
            $"Your WatchingYou booking code — {payment.Reference}",
            $"""
             <p>Hi {contact.FullName},</p>
             <p>Confirm your seats for <strong>{screening.MovieTitle}</strong> with this code:</p>
             <p style="font-size:22px;letter-spacing:4px"><strong>{code}</strong></p>
             <p>{command.Seats.Count} seat(s) · {payment.Amount:0.00} · {brand} ending {last4}</p>
             <p>The seats are held until {payment.ExpiresAtUtc:HH:mm} UTC. After that they go back on sale.</p>
             """), ct);

        return Result.Success(new CheckoutStarted(
            payment.Id, payment.Reference, payment.Amount, brand.ToString(), last4,
            Mask(contact.Email), payment.ExpiresAtUtc));
    }

    /// <summary>Holds that were never confirmed are released. The removal is a soft delete —
    /// the unique index is filtered to [IsDeleted] = 0, so the seat leaves the constraint and
    /// can be sold again, while the abandoned attempt stays on record.</summary>
    private async Task ReleaseExpiredHoldsAsync(Guid screeningId, CancellationToken ct)
    {
        var stale = await db.SeatPayments
            .Include(p => p.Seats)
            .Where(p => p.ScreeningId == screeningId
                     && p.Status == PaymentStatus.AwaitingCode
                     && p.ExpiresAtUtc < DateTime.UtcNow)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        foreach (var payment in stale)
        {
            db.SeatBookings.RemoveRange(payment.Seats);
            payment.Status = PaymentStatus.Expired;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Short, unambiguous, and safe to read aloud at a counter.</summary>
    private static string NewReference()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no I, O, 0 or 1
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return $"WY-{new string(chars)}";
    }

    private static string Mask(string address)
    {
        var at = address.IndexOf('@');
        if (at <= 1) return address;
        return $"{address[0]}{new string('•', Math.Min(at - 1, 6))}{address[at..]}";
    }
}

public sealed record ConfirmBookingCommand(Guid PaymentId, string Code) : ICommand<Result<TicketResponse>>;

internal sealed class ConfirmBookingHandler(CinemaDbContext db, ICurrentUser currentUser)
    : ICommandHandler<ConfirmBookingCommand, Result<TicketResponse>>
{
    public async Task<Result<TicketResponse>> Handle(ConfirmBookingCommand command, CancellationToken ct)
    {
        var payment = await db.SeatPayments
            .Include(p => p.Seats)
            .Include(p => p.Screening)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);

        if (payment is null) return Result.Failure<TicketResponse>(Error.NotFound("Booking"));
        if (payment.UserId != currentUser.RequireId())
            return Result.Failure<TicketResponse>(Error.Forbidden("This booking belongs to someone else."));

        if (payment.Status == PaymentStatus.Confirmed)
            return Result.Success(TicketMapper.ToTicket(payment));

        if (!payment.IsUsable)
        {
            // Expired holds are cleared here too, so the seats do not sit blocked until the
            // next person happens to start a checkout on the same screening.
            if (payment.Status == PaymentStatus.AwaitingCode)
            {
                db.SeatBookings.RemoveRange(payment.Seats);
                payment.Status = PaymentStatus.Expired;
                await db.SaveChangesAsync(ct);
            }
            return Result.Failure<TicketResponse>(Error.Validation("This booking expired. The seats are back on sale."));
        }

        if (!CodeHasher.Verify(command.Code.Trim(), payment.CodeHash, payment.Salt))
        {
            payment.Attempts++;
            await db.SaveChangesAsync(ct);

            return Result.Failure<TicketResponse>(payment.AttemptsLeft == 0
                ? Error.Validation("Too many wrong codes. The seats have been released.")
                : Error.Validation($"Incorrect code. {payment.AttemptsLeft} attempts left."));
        }

        var now = DateTime.UtcNow;
        payment.Status = PaymentStatus.Confirmed;
        payment.ConfirmedAtUtc = now;
        foreach (var seat in payment.Seats) seat.ConfirmedAtUtc = now;

        await db.SaveChangesAsync(ct);
        return Result.Success(TicketMapper.ToTicket(payment));
    }
}

public sealed record CancelBookingCommand(Guid PaymentId) : ICommand<Result>;

internal sealed class CancelBookingHandler(CinemaDbContext db, ICurrentUser currentUser)
    : ICommandHandler<CancelBookingCommand, Result>
{
    public async Task<Result> Handle(CancelBookingCommand command, CancellationToken ct)
    {
        var payment = await db.SeatPayments
            .Include(p => p.Seats)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);

        if (payment is null) return Result.Success();                       // already gone
        if (payment.UserId != currentUser.RequireId())
            return Result.Failure(Error.Forbidden("This booking belongs to someone else."));
        if (payment.Status == PaymentStatus.Confirmed)
            return Result.Failure(Error.Conflict("This booking is already confirmed."));

        // Backing out should free the seats immediately rather than leaving them blocked
        // for the rest of the hold window.
        db.SeatBookings.RemoveRange(payment.Seats);
        payment.Status = PaymentStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record GetMyTicketsQuery : IQuery<IReadOnlyList<TicketResponse>>;

internal sealed class GetMyTicketsHandler(CinemaDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyTicketsQuery, IReadOnlyList<TicketResponse>>
{
    public async Task<IReadOnlyList<TicketResponse>> Handle(GetMyTicketsQuery query, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        // Tickets are re-read from the database rather than held in the page, so a refresh,
        // a new tab or a different device all show the same QR code.
        var payments = await db.SeatPayments.AsNoTracking()
            .Include(p => p.Seats)
            .Include(p => p.Screening)
            .Where(p => p.UserId == userId && p.Status == PaymentStatus.Confirmed)
            .OrderByDescending(p => p.ConfirmedAtUtc)
            .Take(20)
            .ToListAsync(ct);

        return [.. payments.Select(TicketMapper.ToTicket)];
    }
}

internal static class TicketMapper
{
    /// <summary>A1, B7 — the way seats are printed on a ticket and called out in a hall.</summary>
    public static string Label(int row, int number) => $"{(char)('A' + row - 1)}{number}";

    public static TicketResponse ToTicket(SeatPayment payment) => new(
        payment.Id,
        payment.Reference,
        payment.Screening?.MovieTitle ?? "",
        payment.Screening?.Hall ?? "",
        payment.Screening?.StartsAtUtc ?? default,
        payment.Screening?.AudioLanguage ?? "az",
        payment.Screening?.SubtitleLanguage,
        [.. payment.Seats
            .OrderBy(s => s.Row).ThenBy(s => s.Number)
            .Select(s => new TicketSeat(s.Row, s.Number, Label(s.Row, s.Number),
                // Each seat carries its own payload: the booking reference identifies the
                // purchase, the seat label identifies which of its seats this is. Enough for
                // a doorman to check at the gate, and nothing about the card.
                $"WATCHINGYOU|{payment.Reference}|{payment.ScreeningId:N}|{Label(s.Row, s.Number)}"))],
        payment.Amount,
        payment.Brand.ToString(),
        payment.Last4,
        payment.ConfirmedAtUtc ?? DateTime.UtcNow);
}

public sealed record PendingCheckout(
    Guid PaymentId, Guid ScreeningId, string MovieTitle, string Reference, decimal Amount,
    string Brand, string Last4, IReadOnlyList<SeatSelection> Seats, DateTime ExpiresAtUtc);

public sealed record GetPendingCheckoutsQuery : IQuery<IReadOnlyList<PendingCheckout>>;

internal sealed class GetPendingCheckoutsHandler(CinemaDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetPendingCheckoutsQuery, IReadOnlyList<PendingCheckout>>
{
    public async Task<IReadOnlyList<PendingCheckout>> Handle(GetPendingCheckoutsQuery query, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var now = DateTime.UtcNow;

        // A checkout that was paid but never confirmed used to be unreachable after a reload:
        // the seats sat held with no way back to the code screen. Asking the server means the
        // way back survives a refresh, a new tab or a different device.
        var pending = await db.SeatPayments.AsNoTracking()
            .Include(p => p.Seats)
            .Include(p => p.Screening)
            .Where(p => p.UserId == userId && p.Status == PaymentStatus.AwaitingCode && p.ExpiresAtUtc > now)
            .OrderBy(p => p.ExpiresAtUtc)
            .ToListAsync(ct);

        return [.. pending.Select(p => new PendingCheckout(
            p.Id, p.ScreeningId, p.Screening?.MovieTitle ?? "", p.Reference, p.Amount,
            p.Brand.ToString(), p.Last4,
            [.. p.Seats.OrderBy(x => x.Row).ThenBy(x => x.Number).Select(x => new SeatSelection(x.Row, x.Number))],
            p.ExpiresAtUtc))];
    }
}

public static class BookSeatsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/screenings/{id:guid}/checkout",
            async Task<Results<Ok<CheckoutStarted>, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, CheckoutCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { ScreeningId = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            })
        .WithName("StartSeatCheckoutWithId").WithTags("Cinema").RequireAuthorization();

        app.MapPost("/api/bookings/{paymentId:guid}/confirm",
            async Task<Results<Ok<TicketResponse>, BadRequest<Error>, NotFound<Error>, ForbidHttpResult>> (
                Guid paymentId, ConfirmBookingCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { PaymentId = paymentId }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "forbidden" => TypedResults.Forbid(),
                    _ => TypedResults.BadRequest(result.Error)
                };
            })
        .WithName("ConfirmBookingWithId").WithTags("Cinema").RequireAuthorization();

        app.MapPost("/api/bookings/{paymentId:guid}/cancel",
            async Task<Results<NoContent, Conflict<Error>, ForbidHttpResult>> (
                Guid paymentId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new CancelBookingCommand(paymentId), ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "forbidden"
                    ? TypedResults.Forbid()
                    : TypedResults.Conflict(result.Error);
            })
        .WithName("CancelBookingWithId").WithTags("Cinema").RequireAuthorization();

        app.MapGet("/api/bookings/mine", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetMyTicketsQuery(), ct)))
            .WithName("GetMyTickets").WithTags("Cinema").RequireAuthorization();

        app.MapGet("/api/bookings/pending", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetPendingCheckoutsQuery(), ct)))
            .WithName("GetPendingCheckouts").WithTags("Cinema").RequireAuthorization();
    }
}
