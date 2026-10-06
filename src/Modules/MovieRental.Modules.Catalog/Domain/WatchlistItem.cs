using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Catalog.Domain;

/// <summary>
/// "Watch later". One row per customer per film.
///
/// Deliberately not soft-deletable: taking a film off your list is not an event worth keeping,
/// and a hidden row would still hold the unique index and block adding it back.
/// </summary>
public sealed class WatchlistItem : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid MovieId { get; set; }
    public Movie? Movie { get; set; }
}
