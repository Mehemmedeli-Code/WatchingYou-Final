using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

public enum SupportStatus { Open = 1, Answered = 2, Closed = 3 }

/// <summary>
/// One live conversation between a customer and the Security desk — the model a bank's chat
/// window uses, not a ticket queue. A customer has one thread they keep coming back to rather
/// than a new numbered case each time they have a question.
///
/// The e-mail and name are snapshotted so the thread still reads correctly after a rename.
/// Nothing else about the account is copied here. A password does not appear in this file, in
/// this table, or on the screen that renders it: staff answering a question have no business
/// seeing credentials, and a desk that displayed them would deserve to lose the trust it runs on.
/// </summary>
public sealed class SupportConversation : BaseEntity, ISoftDeletable
{
    public Guid UserId { get; set; }
    public required string UserEmail { get; set; }
    public required string UserName { get; set; }

    public SupportStatus Status { get; set; } = SupportStatus.Open;

    /// <summary>Sorts the desk's list, so whoever has been waiting longest is at the top.</summary>
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    public List<SupportChatMessage> Messages { get; set; } = [];

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public sealed class SupportChatMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public SupportConversation? Conversation { get; set; }

    /// <summary>True when the desk wrote it. One flag rather than a role string: there are
    /// exactly two sides to this conversation and a reader only needs to know which.</summary>
    public bool FromDesk { get; set; }

    public Guid AuthorUserId { get; set; }
    public required string AuthorName { get; set; }
    public required string Body { get; set; }

    /// <summary>Set when the other side has seen it, which is what drives the unread count.</summary>
    public DateTime? SeenAtUtc { get; set; }
}
