namespace MovieRental.Modules.Catalog.Features;

public sealed record MovieListItem(
    Guid Id, string Title, string Slug, string Genre, int ReleaseYear, int DurationMinutes,
    decimal DailyPrice, int AvailableCopies, int TotalCopies, double AverageRating, int ReviewCount,
    string? PosterUrl, bool IsDeleted,
    // A flag rather than the URL: the list is public, and a card only needs to know whether
    // to enable its Watch button.
    bool HasVideo);

public sealed record MovieDetail(
    Guid Id, string Title, string Slug, string Description, string Genre, int ReleaseYear,
    int DurationMinutes, string? Director, string? PosterUrl, string? TrailerUrl, string? VideoUrl,
    decimal DailyPrice, int AvailableCopies, int TotalCopies, double AverageRating, int ReviewCount,
    IReadOnlyList<ReviewResponse> Reviews, bool HasVideo = false);

public sealed record ReviewResponse(Guid Id, Guid UserId, string AuthorName, int Stars, string Comment, DateTime CreatedAtUtc);
