using MovieRental.Modules.Cinema.Domain;

namespace MovieRental.Tests;

/// <summary>
/// The refund rule decides how much money goes back, and it is quoted to the customer before
/// it is applied. If the quote and the refund ever disagreed, the customer would be the one to
/// find out — so the rule is a pure function of (booking, screening, now) and these tests pin
/// the edges of it.
/// </summary>
public class RefundPolicyTests
{
    private static readonly RefundPolicy Policy = new();
    private static readonly DateTime Start = new(2026, 6, 1, 20, 0, 0, DateTimeKind.Utc);

    private static Screening ScreeningAt(
        DateTime start, bool cancelled = false, int? window = null, decimal? fee = null) => new()
    {
        MovieTitle = "Blue Hour",
        Hall = "A100",
        StartsAtUtc = start,
        IsCancelled = cancelled,
        RefundWindowHours = window,
        RefundFeePercent = fee
    };

    private static SeatPayment PaymentOf(
        decimal amount = 20m,
        PaymentStatus status = PaymentStatus.Confirmed,
        DateTime? checkedIn = null) => new()
    {
        Reference = "WY-TEST01",
        Amount = amount,
        Last4 = "4242",
        CardHolder = "Test",
        CodeHash = "h",
        Salt = "s",
        Status = status,
        Seats = [new SeatBooking { Row = 1, Number = 1, PricePaid = amount, CheckedInAtUtc = checkedIn }]
    };

    [Fact]
    public void Outside_the_window_a_fee_is_kept()
    {
        // 72 hours out: refundable, 30% of 20.00 kept.
        var quote = Policy.Quote(PaymentOf(), ScreeningAt(Start), Start.AddHours(-72));

        Assert.Equal(RefundOutcome.PartialWithFee, quote.Outcome);
        Assert.True(quote.Allowed);
        Assert.Equal(6.00m, quote.Fee);
        Assert.Equal(14.00m, quote.Refund);
    }

    [Fact]
    public void The_boundary_is_inclusive_at_exactly_the_window()
    {
        // Exactly 48 hours still counts as outside — the customer who reads "more than 48
        // hours" and acts on the hour should not be refused.
        var quote = Policy.Quote(PaymentOf(), ScreeningAt(Start), Start.AddHours(-48));
        Assert.Equal(RefundOutcome.PartialWithFee, quote.Outcome);
    }

    [Fact]
    public void One_hour_inside_the_window_is_refused()
    {
        var quote = Policy.Quote(PaymentOf(), ScreeningAt(Start), Start.AddHours(-47));

        Assert.Equal(RefundOutcome.TooLate, quote.Outcome);
        Assert.False(quote.Allowed);
        Assert.Equal(0m, quote.Refund);
    }

    [Fact]
    public void A_cancelled_screening_refunds_everything_even_inside_the_window()
    {
        // The customer did not change their mind; the cinema did.
        var quote = Policy.Quote(PaymentOf(), ScreeningAt(Start, cancelled: true), Start.AddHours(-2));

        Assert.Equal(RefundOutcome.Full, quote.Outcome);
        Assert.True(quote.Allowed);
        Assert.Equal(20.00m, quote.Refund);
        Assert.Equal(0m, quote.Fee);
    }

    [Fact]
    public void A_cancelled_screening_refunds_even_after_it_should_have_started()
    {
        var quote = Policy.Quote(PaymentOf(), ScreeningAt(Start, cancelled: true), Start.AddHours(3));
        Assert.Equal(RefundOutcome.Full, quote.Outcome);
    }

    [Fact]
    public void A_screening_may_set_its_own_window_and_fee()
    {
        // The per-screening override is why each performance carries its own Rules section.
        var lenient = ScreeningAt(Start, window: 6, fee: 10m);
        var quote = Policy.Quote(PaymentOf(), lenient, Start.AddHours(-8));

        Assert.Equal(RefundOutcome.PartialWithFee, quote.Outcome);
        Assert.Equal(2.00m, quote.Fee);
        Assert.Equal(18.00m, quote.Refund);
    }

    [Fact]
    public void A_scanned_ticket_is_never_refunded()
    {
        // Booked far ahead, but already used at the door.
        var used = PaymentOf(checkedIn: Start.AddHours(-1));
        var quote = Policy.Quote(used, ScreeningAt(Start), Start.AddHours(-72));

        Assert.Equal(RefundOutcome.AlreadyUsed, quote.Outcome);
        Assert.False(quote.Allowed);
    }

    [Fact]
    public void A_scanned_ticket_is_not_refunded_even_when_the_screening_is_cancelled()
    {
        var used = PaymentOf(checkedIn: Start.AddHours(-1));
        var quote = Policy.Quote(used, ScreeningAt(Start, cancelled: true), Start.AddHours(-1));
        Assert.Equal(RefundOutcome.AlreadyUsed, quote.Outcome);
    }

    [Fact]
    public void Refunding_twice_is_refused()
    {
        var quote = Policy.Quote(
            PaymentOf(status: PaymentStatus.Refunded), ScreeningAt(Start), Start.AddHours(-72));

        Assert.Equal(RefundOutcome.AlreadyRefunded, quote.Outcome);
        Assert.False(quote.Allowed);
    }

    [Fact]
    public void An_unconfirmed_hold_has_nothing_to_refund()
    {
        var quote = Policy.Quote(
            PaymentOf(status: PaymentStatus.AwaitingCode), ScreeningAt(Start), Start.AddHours(-72));

        Assert.Equal(RefundOutcome.NotConfirmed, quote.Outcome);
    }

    [Fact]
    public void Once_it_has_started_there_is_no_refund()
    {
        var quote = Policy.Quote(PaymentOf(), ScreeningAt(Start), Start.AddMinutes(1));
        Assert.Equal(RefundOutcome.Started, quote.Outcome);
    }
}
