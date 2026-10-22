namespace MovieRental.Modules.Rentals.Domain;

/// <summary>
/// Feature 4 — late fees. Kept as a pure function of (rental, now) with no database or
/// clock of its own, so it is trivially unit-testable and the same rule is applied by the
/// return endpoint, the read model and the nightly job.
/// </summary>
public sealed class LateFeePolicy
{
    public decimal DailyRateMultiplier { get; init; } = 1.5m;
    public decimal GraceHours { get; init; } = 6;
    public decimal MaxFeeMultiplier { get; init; } = 10m;

    public decimal Calculate(Rental rental, DateTime nowUtc)
    {
        var reference = rental.ReturnedAtUtc ?? nowUtc;
        var overdue = reference - rental.DueAtUtc;
        if (overdue.TotalHours <= (double)GraceHours) return 0m;

        var chargeableDays = (int)Math.Ceiling(overdue.TotalDays);
        var fee = rental.DailyPrice * DailyRateMultiplier * chargeableDays;

        // Cap the fee: an unreturned disc should never cost more than replacing it.
        var cap = rental.DailyPrice * MaxFeeMultiplier;
        return Math.Round(Math.Min(fee, cap), 2);
    }
}
