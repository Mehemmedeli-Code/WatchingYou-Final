namespace MovieRental.Modules.Rentals.Domain;

/// <summary>
/// Late fees — of which there are none. When a rental's paid three days run out, the renter is
/// asked whether to keep the film three more days (another $0.50) or give it back, and nothing
/// is charged while that question is open, however long it stays open. The only money a rental
/// ever costs is the $0.50 periods the renter chose to pay for (<see cref="RentalPricing"/>).
///
/// The class stays because the return endpoint, the rental list and the nightly notifier all
/// ask it the same question; one place answering "0" keeps them agreeing.
/// </summary>
public sealed class LateFeePolicy
{
    public decimal Calculate(Rental rental, DateTime nowUtc) => 0m;
}
