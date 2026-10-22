using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Catalog.Domain;

public sealed class Movie : BaseEntity, ISoftDeletable
{
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string Description { get; set; } = string.Empty;
    public required string Genre { get; set; }
    public int ReleaseYear { get; set; }
    public int DurationMinutes { get; set; }
    public string? Director { get; set; }
    public string? PosterUrl { get; set; }
    public string? TrailerUrl { get; set; }

    public decimal DailyPrice { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }

    /// <summary>Denormalised average of <see cref="Review.Stars"/>. Recomputed on write so
    /// the catalogue query never has to aggregate across reviews.</summary>
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public List<Review> Reviews { get; set; } = [];

    public bool IsAvailable => AvailableCopies > 0;
}
