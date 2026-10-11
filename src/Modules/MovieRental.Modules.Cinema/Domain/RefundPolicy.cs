namespace MovieRental.Modules.Cinema.Domain;

public enum RefundOutcome
{
    /// <summary>Everything back: cancelled early enough, or the screening itself was cancelled.</summary>
    Full = 1,
    /// <summary>Cancelled late: refundable, less the fee.</summary>
    PartialWithFee = 2,
    /// <summary>No longer produced by the policy (late cancellations are partial now); kept so
    /// the stored and serialised names stay stable.</summary>
    TooLate = 3,
    AlreadyRefunded = 4,
    NotConfirmed = 5,
    /// <summary>Scanned at the door. Whatever happened after that, the seat was used.</summary>
    AlreadyUsed = 6,
    Started = 7
}

public sealed record RefundQuote(
    RefundOutcome Outcome, decimal Refund, decimal Fee, int HoursUntilStart, bool Allowed);

/// <summary>
/// What a customer gets back, and when.
///
/// A pure function of (booking, screening, now) — no clock of its own and no database — so the
/// quote shown on the ticket and the amount actually refunded are computed by the same code.
/// Two implementations would eventually disagree, and the customer would be the one to find out.
///
/// Defaults: everything back up to 24 hours before the start, 70% back (30% kept) from then
/// until the start, nothing once it has started, and everything back if the screening is
/// cancelled. A screening may override the window and the fee — which is why its own page carries a Rules section rather than the site quoting one
/// policy everywhere.
/// </summary>
public sealed class RefundPolicy
{
    /// <summary>Hours before the start until which a refund is in full.</summary>
    public const int DefaultWindowHours = 24;
    /// <summary>Kept when cancelling inside the window.</summary>
    public const decimal DefaultFeePercent = 30m;

    public RefundQuote Quote(SeatPayment payment, Screening screening, DateTime nowUtc)
    {
        var hours = (int)Math.Floor((screening.StartsAtUtc - nowUtc).TotalHours);

        if (payment.Status == PaymentStatus.Refunded)
            return new RefundQuote(RefundOutcome.AlreadyRefunded, 0, 0, hours, false);

        if (payment.Status != PaymentStatus.Confirmed)
            return new RefundQuote(RefundOutcome.NotConfirmed, 0, 0, hours, false);

        // Checked in before the cancellation request: the seat was occupied, whatever the clock
        // says now.
        if (payment.Seats.Any(seat => seat.CheckedInAtUtc is not null))
            return new RefundQuote(RefundOutcome.AlreadyUsed, 0, payment.Amount, hours, false);

        // A cancelled screening overrides every other rule, including the cut-off. The customer
        // did not change their mind — we changed ours.
        if (screening.IsCancelled)
            return new RefundQuote(RefundOutcome.Full, payment.Amount, 0, hours, true);

        if (screening.StartsAtUtc <= nowUtc)
            return new RefundQuote(RefundOutcome.Started, 0, payment.Amount, hours, false);

        // Exactly on the hour still counts as early: "24 hours before" read and acted on the
        // hour is not refused the full amount.
        var window = screening.RefundWindowHours ?? DefaultWindowHours;
        if (hours >= window)
            return new RefundQuote(RefundOutcome.Full, payment.Amount, 0, hours, true);

        var percent = Math.Clamp(screening.RefundFeePercent ?? DefaultFeePercent, 0m, 100m);
        var fee = Math.Round(payment.Amount * percent / 100m, 2);

        return new RefundQuote(RefundOutcome.PartialWithFee, payment.Amount - fee, fee, hours, true);
    }
}
