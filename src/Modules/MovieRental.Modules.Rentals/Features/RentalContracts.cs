using MovieRental.Modules.Rentals.Domain;

namespace MovieRental.Modules.Rentals.Features;

public sealed record RentalResponse(
    Guid Id, Guid MovieId, string MovieTitle, string? PosterUrl,
    DateTime RentedAtUtc, DateTime DueAtUtc, DateTime? ReturnedAtUtc,
    decimal DailyPrice, decimal BasePrice, decimal LateFee, decimal TotalDue,
    int DaysOverdue, int ExtensionCount, RentalStatus Status);
