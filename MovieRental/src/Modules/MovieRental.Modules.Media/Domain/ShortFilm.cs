using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Media.Domain;

/// <summary>
/// Two stages, two people. Security inspects and records findings; Admin publishes. A
/// submission can only move forward one step at a time, so nothing reaches the public
/// galleries without a stored inspection behind it.
/// </summary>
public enum SubmissionStatus
{
    Pending = 1,
    UnderSecurityReview = 2,
    SecurityCleared = 3,
    SecurityFlagged = 4,
    Approved = 5,
    Rejected = 6,
    Expired = 7
}

/// <summary>Declared by the uploader, correctable by the admin. Decides which gallery a
/// published film lands in and nothing else.</summary>
public enum ShortFilmOrigin { AiGenerated = 1, HandCrafted = 2 }

public enum ShortFilmVisibility { Private = 1, Public = 2 }

public sealed class ShortFilm : BaseEntity, ISoftDeletable
{
    public Guid UserId { get; set; }
    public required string AuthorName { get; set; }
    public required string Title { get; set; }
    public string Synopsis { get; set; } = string.Empty;

    public required string StoredFileName { get; set; }
    public required string OriginalFileName { get; set; }
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "video/mp4";

    public ShortFilmOrigin Origin { get; set; } = ShortFilmOrigin.HandCrafted;
    public ShortFilmVisibility Visibility { get; set; } = ShortFilmVisibility.Private;
    public int ViewCount { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Every submission gets an answer within three days. The deadline covers the
    /// whole pipeline, not each stage, and is stored rather than computed so shifting the
    /// SLA later never rewrites old promises.</summary>
    public DateTime ReviewDeadlineUtc { get; set; } = DateTime.UtcNow.AddDays(3);

    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewerNote { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public SecurityReport? SecurityReport { get; set; }
    public List<SubmissionComment> Comments { get; set; } = [];

    public bool IsDecided => Status is SubmissionStatus.Approved or SubmissionStatus.Rejected;

    /// <summary>The only condition under which a stranger may see this film.</summary>
    public bool IsPublished => Status == SubmissionStatus.Approved && Visibility == ShortFilmVisibility.Public;

    public int HoursLeft(DateTime nowUtc) =>
        IsDecided ? 0 : Math.Max(0, (int)(ReviewDeadlineUtc - nowUtc).TotalHours);
}
