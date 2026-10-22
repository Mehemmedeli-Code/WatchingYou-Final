namespace MovieRental.Modules.Catalog.Features;

public sealed record MovieListItem(
    Guid Id, string Title, string Slug, string Genre, int ReleaseYear, int DurationMinutes,
    decimal DailyPrice, int AvailableCopies, int TotalCopies, double AverageRating, int ReviewCount,
    string? PosterUrl, bool IsDeleted);

public sealed record MovieDetail(
    Guid Id, string Title, string Slug, string Description, string Genre, int ReleaseYear,
    int DurationMinutes, string? Director, string? PosterUrl, string? TrailerUrl,
    decimal DailyPrice, int AvailableCopies, int TotalCopies, double AverageRating, int ReviewCount,
    IReadOnlyList<ReviewResponse> Reviews);

public sealed record ReviewResponse(Guid Id, Guid UserId, string AuthorName, int Stars, string Comment, DateTime CreatedAtUtc);
