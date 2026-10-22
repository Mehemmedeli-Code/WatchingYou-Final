using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Catalog.Domain;

public sealed class Review : BaseEntity, ISoftDeletable
{
    public Guid MovieId { get; set; }
    public Movie? Movie { get; set; }

    public Guid UserId { get; set; }
    public required string AuthorName { get; set; }
    public int Stars { get; set; }
    public string Comment { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
