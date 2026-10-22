using MovieRental.Modules.Cinema.Domain;

namespace MovieRental.Tests;

/// <summary>
/// The checkout, the price preview and the e-mail all read their numbers from Pricing, so these
/// tests are the contract: what a code takes off, how far points go, and what is earned back.
/// </summary>
public class PricingTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PromoCode Percent(decimal percent, decimal min = 0, int? max = null, int used = 0) =>
        new() { Code = "TEST", PercentOff = percent, MinSubtotal = min, MaxRedemptions = max, Redemptions = used };

    private static PromoCode Amount(decimal amount, decimal min = 0) =>
        new() { Code = "FLAT", AmountOff = amount, MinSubtotal = min };

    [Fact]
    public void No_discount_charges_seats_times_price_and_earns_a_point_per_whole_manat()
    {
        var price = Pricing.Calculate(8.50m, 3, null, Now, 0, false);

        Assert.Equal(25.50m, price.Subtotal);
        Assert.Equal(25.50m, price.Total);
        Assert.Equal(25, price.PointsEarned);
    }

    [Fact]
    public void Percentage_code_is_rounded_to_the_cent()
    {
        var price = Pricing.Calculate(8.50m, 3, Percent(15), Now, 0, false);

        Assert.Equal(3.83m, price.PromoDiscount);       // 25.50 × 15% = 3.825
        Assert.Equal(21.67m, price.Total);
    }

    [Fact]
    public void Fixed_code_never_takes_the_total_below_zero()
    {
        var price = Pricing.Calculate(4m, 1, Amount(10), Now, 0, false);

        Assert.Equal(4m, price.PromoDiscount);
        Assert.Equal(0m, price.Total);
        Assert.Equal(0, price.PointsEarned);
    }

    [Theory]
    [InlineData(PromoRejection.BelowMinimum)]
    [InlineData(PromoRejection.UsedUp)]
    [InlineData(PromoRejection.Expired)]
    [InlineData(PromoRejection.NotYetValid)]
    [InlineData(PromoRejection.Inactive)]
    public void A_code_that_does_not_apply_takes_nothing_off_and_says_why(PromoRejection expected)
    {
        var promo = Percent(20, min: expected == PromoRejection.BelowMinimum ? 100 : 0,
            max: expected == PromoRejection.UsedUp ? 5 : null, used: expected == PromoRejection.UsedUp ? 5 : 0);
        if (expected == PromoRejection.Expired) promo.ValidUntilUtc = Now.AddDays(-1);
        if (expected == PromoRejection.NotYetValid) promo.ValidFromUtc = Now.AddDays(1);
        if (expected == PromoRejection.Inactive) promo.IsActive = false;

        var price = Pricing.Calculate(10m, 2, promo, Now, 0, false);

        Assert.Equal(expected, price.PromoRejection);
        Assert.Equal(0m, price.PromoDiscount);
        Assert.Equal(20m, price.Total);
        Assert.False(string.IsNullOrEmpty(Pricing.Explain(expected)));
    }

    [Fact]
    public void Points_pay_for_at_most_half_of_what_is_left_after_the_code()
    {
        // 20.00 − 10% = 18.00; half of that is 9.00 = 180 points at 0.05 each.
        var price = Pricing.Calculate(10m, 2, Percent(10), Now, pointsBalance: 1000, usePoints: true);

        Assert.Equal(180, price.PointsUsed);
        Assert.Equal(9.00m, price.PointsDiscount);
        Assert.Equal(9.00m, price.Total);
        Assert.Equal(9, price.PointsEarned);            // earned on what was actually paid
    }

    [Fact]
    public void Points_are_limited_by_the_balance()
    {
        var price = Pricing.Calculate(10m, 2, null, Now, pointsBalance: 40, usePoints: true);

        Assert.Equal(40, price.PointsUsed);
        Assert.Equal(2.00m, price.PointsDiscount);
        Assert.Equal(18.00m, price.Total);
    }

    [Fact]
    public void Points_are_not_touched_unless_asked()
    {
        var price = Pricing.Calculate(10m, 2, null, Now, pointsBalance: 500, usePoints: false);

        Assert.Equal(0, price.PointsUsed);
        Assert.Equal(20m, price.Total);
    }
}
