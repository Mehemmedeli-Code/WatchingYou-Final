namespace MovieRental.Modules.Identity.Domain;

/// <summary>One person following another. The pair is the key, so following twice is a no-op
/// rather than a duplicate row, and unfollowing is a plain delete.</summary>
public sealed class Follow
{
    public Guid FollowerId { get; set; }
    public Guid FolloweeId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>False while it is only a request to a private account; counts and lists only
    /// ever include accepted rows. Declining deletes the row.</summary>
    public bool IsAccepted { get; set; } = true;
}
