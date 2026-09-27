namespace MovieRental.Modules.Cinema.Domain;

public enum RefundOutcome
{
    /// <summary>Everything back. The screening was cancelled, so the customer risked nothing.</summary>
    Full = 1,
    /// <summary>Refundable, less the cancellation fee.</summary>
    PartialWithFee = 2,
    /// <summary>Inside the cut-off; the seat can no longer be resold in time.</summary>
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
/// Defaults: free up to 48 hours before, 30% kept after that, nothing inside the window, and
/// everything back if the screening is cancelled. A screening may override the window and the
/// fee — which is why its own page carries a Rules section rather than the site quoting one
/// policy everywhere.
/// </summary>
public sealed class RefundPolicy
{
    public const int DefaultWindowHours = 48;
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

        var window = screening.RefundWindowHours ?? DefaultWindowHours;
        if (hours < window)
            return new RefundQuote(RefundOutcome.TooLate, 0, payment.Amount, hours, false);

        var percent = Math.Clamp(screening.RefundFeePercent ?? DefaultFeePercent, 0m, 100m);
        var fee = Math.Round(payment.Amount * percent / 100m, 2);

        return new RefundQuote(RefundOutcome.PartialWithFee, payment.Amount - fee, fee, hours, true);
    }
}
