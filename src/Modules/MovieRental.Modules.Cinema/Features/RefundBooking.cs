using System.Text.Json.Serialization;
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

// Giving a ticket back.
//
// The quote and the refund are the same calculation, asked twice: once to show the customer
// what they would get, once to carry it out. Anything else eventually shows one number and
// pays another.

public sealed record RefundTerms(
    string Outcome, decimal Refund, decimal Fee, int HoursUntilStart, bool Allowed,
    int WindowHours, decimal FeePercent, string? Note, bool ScreeningCancelled);

public sealed record GetRefundTermsQuery(Guid PaymentId) : IQuery<RefundTerms?>;

internal sealed class GetRefundTermsHandler(CinemaDbContext db, ICurrentUser currentUser, RefundPolicy policy)
    : IQueryHandler<GetRefundTermsQuery, RefundTerms?>
{
    public async Task<RefundTerms?> Handle(GetRefundTermsQuery query, CancellationToken ct)
    {
        var payment = await db.SeatPayments.AsNoTracking()
            .Include(p => p.Seats)
            .Include(p => p.Screening)
            .FirstOrDefaultAsync(p => p.Id == query.PaymentId, ct);

        if (payment is null || payment.Screening is null) return null;
        if (payment.UserId != currentUser.RequireId()) return null;

        return Describe(policy.Quote(payment, payment.Screening, DateTime.UtcNow), payment.Screening);
    }

    internal static RefundTerms Describe(RefundQuote quote, Screening screening) => new(
        quote.Outcome.ToString(), quote.Refund, quote.Fee, quote.HoursUntilStart, quote.Allowed,
        screening.RefundWindowHours ?? RefundPolicy.DefaultWindowHours,
        screening.RefundFeePercent ?? RefundPolicy.DefaultFeePercent,
        screening.RefundNote,
        screening.IsCancelled);
}

/// <param name="PaymentId">From the address; not read from the body.</param>
public sealed record RefundBookingCommand([property: JsonIgnore] Guid PaymentId, string? Reason = null) : ICommand<Result<RefundTerms>>;

internal sealed class RefundBookingValidator : AbstractValidator<RefundBookingCommand>
{
    public RefundBookingValidator() => RuleFor(x => x.Reason).MaximumLength(300);
}

internal sealed class RefundBookingHandler(
    CinemaDbContext db, ICurrentUser currentUser, RefundPolicy policy, IUserDirectory users,
    IEmailSender email, IAuditLog audit, BookingFinalizer finalizer, IStripeGateway stripe)
    : ICommandHandler<RefundBookingCommand, Result<RefundTerms>>
{
    public async Task<Result<RefundTerms>> Handle(RefundBookingCommand command, CancellationToken ct)
    {
        var payment = await db.SeatPayments
            .Include(p => p.Seats)
            .Include(p => p.Screening!).ThenInclude(s => s.HallRoom!).ThenInclude(h => h.Venue)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);

        if (payment is null || payment.Screening is null)
            return Result.Failure<RefundTerms>(Error.NotFound("Booking"));

        if (payment.UserId != currentUser.RequireId())
            return Result.Failure<RefundTerms>(Error.Forbidden("This booking belongs to someone else."));

        var screening = payment.Screening;

        // Recomputed here rather than trusting anything the client sent: the customer may have
        // been looking at the quote for an hour, and the cut-off moves while they look.
        var quote = policy.Quote(payment, screening, DateTime.UtcNow);

        if (!quote.Allowed)
            return Result.Failure<RefundTerms>(Error.Conflict(Explain(quote, screening)));

        // Claim the refund before any money moves. Two refund requests at once both passed the
        // checks above; only the one whose conditional UPDATE hits the row goes on, so points are
        // restored and Stripe is asked once.
        var refundedAt = DateTime.UtcNow;
        var claimed = await db.SeatPayments.IgnoreQueryFilters()
            .Where(p => p.Id == payment.Id && p.Status == PaymentStatus.Confirmed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Refunded)
                .SetProperty(p => p.RefundedAtUtc, refundedAt), ct);
        if (claimed == 0)
            return Result.Failure<RefundTerms>(Error.Conflict("This booking has already been refunded."));

        // Money paid through Stripe has to go back through Stripe, and before anything here
        // changes: if Stripe refuses, the booking must stay exactly as it was.
        if (payment.Provider == PaymentProvider.Stripe && quote.Refund > 0)
        {
            if (string.IsNullOrEmpty(payment.ExternalPaymentId))
                return Result.Failure<RefundTerms>(Error.Conflict("This Stripe payment has no payment reference; ask the help desk."));
            try
            {
                await stripe.RefundAsync(payment.ExternalPaymentId, quote.Refund, ct);
            }
            catch (Exception ex) when (ex is StripeException or HttpRequestException or TaskCanceledException)
            {
                // Stripe refused: give the booking back exactly as it was.
                await db.SeatPayments.IgnoreQueryFilters()
                    .Where(p => p.Id == payment.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, PaymentStatus.Confirmed)
                        .SetProperty(p => p.RefundedAtUtc, (DateTime?)null), CancellationToken.None);
                return Result.Failure<RefundTerms>(Error.Conflict($"Stripe could not refund the payment: {ex.Message}"));
            }
        }

        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAtUtc = refundedAt;
        payment.RefundedAmount = quote.Refund;
        payment.RefundReason = command.Reason?.Trim();

        // Seats go back on sale. Soft-deleted, so the unique index releases them while the
        // booking itself stays on record.
        db.SeatBookings.RemoveRange(payment.Seats);
        await finalizer.ReverseLoyaltyAsync(payment, ct);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry("booking.refunded",
            $"{payment.Reference} · {screening.MovieTitle}",
            $"{quote.Refund:0.00} returned, {quote.Fee:0.00} kept — {quote.Outcome}", payment.Id), ct);

        await NotifyAsync(payment, screening, quote, ct);

        return Result.Success(GetRefundTermsHandler.Describe(quote, screening));
    }

    private static string Explain(RefundQuote quote, Screening screening) => quote.Outcome switch
    {
        RefundOutcome.AlreadyRefunded => "This booking has already been refunded.",
        RefundOutcome.AlreadyUsed => "These seats were scanned at the door, so they cannot be refunded.",
        RefundOutcome.Started => "The screening has already started.",
        _ => "This booking cannot be refunded."
    };

    private async Task NotifyAsync(SeatPayment payment, Screening screening, RefundQuote quote, CancellationToken ct)
    {
        var contact = await users.GetContactAsync(payment.UserId, ct);
        if (contact is null) return;

        var hours = screening.RefundWindowHours ?? RefundPolicy.DefaultWindowHours;
        var feeLine = quote.Fee > 0
            ? "<p>" + Lang.Pick(
                $"Bron seansdan {hours} saatdan az qalmış ləğv edildiyi üçün {quote.Fee:0.00} AZN ləğv haqqı tutuldu.",
                $"A cancellation fee of {quote.Fee:0.00} AZN was kept, as the booking was cancelled less than {hours} hours before the screening.",
                $"Удержан сбор за отмену {quote.Fee:0.00} AZN: бронь отменена меньше чем за {hours} ч до сеанса.",
                $"Rezervasyon seanstan {hours} saatten az kala iptal edildiği için {quote.Fee:0.00} AZN iptal ücreti alındı.") + "</p>"
            : screening.IsCancelled
                ? "<p>" + Lang.Pick("Seans ləğv olundu, ona görə məbləğin hamısı qaytarılır — haqq tutulmur.",
                                    "The screening was cancelled, so the full amount goes back — no fee.",
                                    "Сеанс отменён, поэтому вся сумма возвращается — без сбора.",
                                    "Seans iptal edildi, bu yüzden tutarın tamamı iade ediliyor — ücret yok.") + "</p>"
                : "";
        var card = payment.Provider == PaymentProvider.Stripe
            ? Lang.Pick("Stripe ilə ödədiyiniz karta", "the card you paid with through Stripe", "на карту, которой вы платили через Stripe", "Stripe ile ödediğiniz karta")
            : Lang.Pick($"sonu {payment.Last4} olan karta", $"the card ending {payment.Last4}", $"на карту, оканчивающуюся на {payment.Last4}", $"sonu {payment.Last4} olan karta");

        await email.SendAsync(new EmailRequest(contact.Email,
            Lang.Pick($"Pul qaytarıldı — {payment.Reference}", $"Refunded — {payment.Reference}", $"Возврат — {payment.Reference}", $"İade edildi — {payment.Reference}"),
            $"""
             <p>{Lang.Pick("Salam", "Hi", "Здравствуйте", "Merhaba")} {System.Net.WebUtility.HtmlEncode(contact.FullName)},</p>
             <p>{Lang.Pick("Bu film üçün bronunuz ləğv olundu:", "Your booking has been cancelled for", "Ваша бронь отменена:", "Rezervasyonunuz iptal edildi:")} <strong>{screening.MovieTitle}</strong></p>
             <p>{Lang.Pick($"<strong>{quote.Refund:0.00} AZN</strong> {card} qaytarılır.", $"<strong>{quote.Refund:0.00} AZN</strong> is on its way back to {card}.",
                           $"<strong>{quote.Refund:0.00} AZN</strong> возвращается {card}.", $"<strong>{quote.Refund:0.00} AZN</strong> {card} iade ediliyor.")}</p>
             {feeLine}
             <p>{Lang.Pick($"Kod {payment.Reference}. Yerlər yenidən satışdadır.", $"Reference {payment.Reference}. The seats are back on sale.",
                           $"Код {payment.Reference}. Места снова в продаже.", $"Kod {payment.Reference}. Koltuklar yeniden satışta.")}</p>
             """), ct);
    }
}

public static class RefundEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/bookings/{paymentId:guid}/refund-terms", async (
                Guid paymentId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var terms = await dispatcher.Ask(new GetRefundTermsQuery(paymentId), ct);
                return terms is null
                    ? (Results<Ok<RefundTerms>, NotFound<Error>>)TypedResults.NotFound(Error.NotFound("Booking"))
                    : TypedResults.Ok(terms);
            })
            .WithName("GetRefundTermsWithId").WithTags("Cinema").RequireAuthorization()
            .WithSummary("Can this booking be refunded, and how much would come back")
            .WithDescription("Only for your own booking; anyone else's is a 404.");

        app.MapPost("/api/bookings/{paymentId:guid}/refund",
            async Task<Results<Ok<RefundTerms>, Conflict<Error>, NotFound<Error>, JsonHttpResult<Error>>> (
                Guid paymentId, RefundBookingCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { PaymentId = paymentId }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "forbidden" => TypedResults.Json(result.Error, statusCode: StatusCodes.Status403Forbidden),
                    _ => TypedResults.Conflict(result.Error)
                };
            })
            .WithName("RefundBookingWithId").WithTags("Cinema").RequireAuthorization()
            .WithSummary("Refund my booking and free its seats")
            .WithDescription("`reason` optional (up to 300 characters). A refund twice at once refunds once. 403: someone else's. " +
                             "409: already refunded, or too close to the start (the terms say until when).")
            .Produces<Error>(StatusCodes.Status403Forbidden);
    }
}
