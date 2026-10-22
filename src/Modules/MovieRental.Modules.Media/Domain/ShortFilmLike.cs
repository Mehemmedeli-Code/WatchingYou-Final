namespace MovieRental.Modules.Media.Domain;

/// <summary>One person liking one short film. The pair is the key: a second like is the same row.</summary>
public sealed class ShortFilmLike
{
    public Guid ShortFilmId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
