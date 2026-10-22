namespace MovieRental.SharedKernel.Contracts;

/// <summary>
/// What the Rentals module will answer about a customer's history, for modules that need it
/// without owning it. Catalog uses this to decide who may review a film.
/// </summary>
public interface IRentalApi
{
    /// <summary>True if this customer has ever taken the film out. Past rentals count as much
    /// as current ones — having returned it is not a reason to lose your say.</summary>
    Task<bool> HasRentedAsync(Guid userId, Guid movieId, CancellationToken ct = default);

    /// <summary>Every film this customer has ever taken out. Catalog turns these into genres;
    /// Rentals does not know what a genre is, and should not have to.</summary>
    Task<IReadOnlyList<Guid>> RentedMovieIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Whether this person may play the film right now, and why. Catalog asks before
    /// handing out a video address; Rentals owns both rentals and Watching PRO.</summary>
    Task<WatchAccess> GetWatchAccessAsync(Guid userId, Guid movieId, CancellationToken ct = default);
}

public enum WatchAccess
{
    /// <summary>No rental, no subscription.</summary>
    None = 0,
    /// <summary>Watching PRO is active: every film, as often as they like.</summary>
    Pro = 1,
    /// <summary>A rental inside its paid three days.</summary>
    Rental = 2,
    /// <summary>The paid days are over and the renter has not yet chosen: +3 days or return.
    /// Nothing is charged while they decide, and nothing plays either.</summary>
    AwaitingDecision = 3,
}
