namespace MovieRental.SharedKernel.Contracts;

/// <summary>
/// The only way another module may touch the catalogue. Modules depend on this
/// contract in the shared kernel, never on each other's projects, so the dependency
/// graph stays acyclic and a module can later be lifted out without a rewrite.
/// </summary>
public interface ICatalogApi
{
    Task<MovieSummary?> GetMovieAsync(Guid movieId, CancellationToken ct = default);

    /// <summary>Atomically takes one copy off the shelf. Returns false when none are left.</summary>
    Task<bool> TryReserveCopyAsync(Guid movieId, CancellationToken ct = default);

    Task ReleaseCopyAsync(Guid movieId, CancellationToken ct = default);

    Task RecalculateRatingAsync(Guid movieId, CancellationToken ct = default);
}

public sealed record MovieSummary(
    Guid Id, string Title, int ReleaseYear, string Genre, decimal DailyPrice, int AvailableCopies, string? PosterUrl);
