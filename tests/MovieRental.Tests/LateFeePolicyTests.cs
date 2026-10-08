using MovieRental.Modules.Rentals.Domain;

namespace MovieRental.Tests;

/// <summary>
/// The rule since Watching PRO: a rental costs $0.50 per three days the renter chose to pay
/// for, and nothing else. Once the three days are up the renter is asked "+3 days or return?",
/// and no money is added while the question waits for an answer.
/// </summary>
public class LateFeePolicyTests
{
    private static readonly LateFeePolicy Policy = new();
    private static readonly DateTime Due = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Rental RentalDueAt(DateTime due, DateTime? returned = null) => new()
    {
        MovieTitle = "Blue Hour",
        DueAtUtc = due,
        DailyPrice = RentalPricing.PeriodPrice,
        BasePrice = RentalPricing.PeriodPrice,
        ReturnedAtUtc = returned
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(24 * 365)]
    public void Waiting_to_decide_never_costs_anything(int hoursPastDue)
    {
        var fee = Policy.Calculate(RentalDueAt(Due), Due.AddHours(hoursPastDue));
        Assert.Equal(0m, fee);
    }

    [Fact]
    public void Returning_late_costs_nothing_extra()
    {
        var fee = Policy.Calculate(RentalDueAt(Due, returned: Due.AddDays(10)), Due.AddDays(11));
        Assert.Equal(0m, fee);
    }

    [Fact]
    public void The_prices_are_the_advertised_ones()
    {
        Assert.Equal(3, RentalPricing.PeriodDays);
        Assert.Equal(0.50m, RentalPricing.PeriodPrice);
        Assert.Equal(5.00m, RentalPricing.MonthlyPrice);
        Assert.Equal("Watching PRO", RentalPricing.PlanName);
    }
}
