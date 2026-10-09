namespace MovieRental.Modules.Rentals.Domain;

/// <summary>
/// The two ways to watch, and what they cost. Kept in one place so the rent endpoint, the
/// extend endpoint, the subscription and the page that explains them cannot disagree.
///
///  • Free account: rent a film for three days for $0.50. When the three days are up the film
///    stays in the library and the renter is asked: three more days for another $0.50, or give
///    it back. Nothing is charged while the question is open — however long that takes.
///  • Watching PRO: $5 a month, every film, as often as you like.
/// </summary>
public static class RentalPricing
{
    public const string Currency = "USD";

    public const int PeriodDays = 3;
    public const decimal PeriodPrice = 0.50m;

    public const string PlanName = "Watching PRO";
    public const decimal MonthlyPrice = 5.00m;
}
