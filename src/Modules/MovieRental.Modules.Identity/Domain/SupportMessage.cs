using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

public enum SupportStatus { Open = 1, Answered = 2, Closed = 3 }

/// <summary>
/// A message from a customer to the Security desk.
///
/// The sender's e-mail and name are snapshotted at the time of writing, so a thread still
/// reads correctly after somebody changes their display name. Nothing else about the account
/// is copied here — and a password never appears anywhere in this file, this table, or the
/// screen that renders it. Staff answering a question have no business seeing credentials,
/// and a support desk that displayed them would deserve to lose the trust it needs.
/// </summary>
public sealed class SupportMessage : BaseEntity, ISoftDeletable
{
    public Guid UserId { get; set; }
    public required string UserEmail { get; set; }
    public required string UserName { get; set; }

    public required string Subject { get; set; }
    public required string Body { get; set; }

    public SupportStatus Status { get; set; } = SupportStatus.Open;
    public DateTime? AnsweredAtUtc { get; set; }

    public List<SupportReply> Replies { get; set; } = [];

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public sealed class SupportReply : BaseEntity
{
    public Guid MessageId { get; set; }
    public SupportMessage? Message { get; set; }

    public Guid AuthorUserId { get; set; }
    public required string AuthorName { get; set; }

    /// <summary>"Customer" or "Security" — shown as a badge so a reader can tell who is who.</summary>
    public required string AuthorRole { get; set; }

    public required string Body { get; set; }
}
