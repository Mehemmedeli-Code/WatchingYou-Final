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

public sealed record RefundBookingCommand(Guid PaymentId, string? Reason) : ICommand<Result<RefundTerms>>;

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
                return Result.Failure<RefundTerms>(Error.Conflict($"Stripe could not refund the payment: {ex.Message}"));
            }
        }

        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAtUtc = DateTime.UtcNow;
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
        RefundOutcome.TooLate =>
            $"Refunds close {screening.RefundWindowHours ?? RefundPolicy.DefaultWindowHours} hours before the start. " +
            $"There {(quote.HoursUntilStart == 1 ? "is" : "are")} {Math.Max(quote.HoursUntilStart, 0)} left.",
        _ => "This booking cannot be refunded."
    };

    private async Task NotifyAsync(SeatPayment payment, Screening screening, RefundQuote quote, CancellationToken ct)
    {
        var contact = await users.GetContactAsync(payment.UserId, ct);
        if (contact is null) return;

        var feeLine = quote.Fee > 0
            ? $"<p>A cancellation fee of {quote.Fee:0.00} was kept, as the booking was cancelled more than " +
              $"{screening.RefundWindowHours ?? RefundPolicy.DefaultWindowHours} hours before the screening.</p>"
            : screening.IsCancelled
                ? "<p>The screening was cancelled, so the full amount goes back — no fee.</p>"
                : "";

        await email.SendAsync(new EmailRequest(contact.Email,
            $"Refunded — {payment.Reference}",
            $"""
             <p>Hi {contact.FullName},</p>
             <p>Your booking for <strong>{screening.MovieTitle}</strong> has been cancelled.</p>
             <p><strong>{quote.Refund:0.00}</strong> is on its way back to {(payment.Provider == PaymentProvider.Stripe ? "the card you paid with through Stripe" : $"the card ending {payment.Last4}")}.</p>
             {feeLine}
             <p>Reference {payment.Reference}. The seats are back on sale.</p>
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
                return terms is null ? Results.NotFound() : Results.Ok(terms);
            })
            .WithName("GetRefundTermsWithId").WithTags("Cinema").RequireAuthorization();

        app.MapPost("/api/bookings/{paymentId:guid}/refund",
            async Task<Results<Ok<RefundTerms>, Conflict<Error>, NotFound<Error>, ForbidHttpResult>> (
                Guid paymentId, RefundBookingCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { PaymentId = paymentId }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "forbidden" => TypedResults.Forbid(),
                    _ => TypedResults.Conflict(result.Error)
                };
            })
            .WithName("RefundBookingWithId").WithTags("Cinema").RequireAuthorization();
    }
}
