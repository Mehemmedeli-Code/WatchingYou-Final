using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

/// <summary>
/// A conversation between two members.
///
/// The pair is stored sorted — the smaller id always in <see cref="LowUserId"/> — so a thread
/// is found the same way whoever opens it, and a unique index can stop two threads existing
/// for one pair. Without that, both people can start a conversation at the same moment and end
/// up talking into separate rooms.
/// </summary>
public sealed class DirectThread : BaseEntity, ISoftDeletable
{
    public Guid LowUserId { get; set; }
    public Guid HighUserId { get; set; }

    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    public List<DirectMessage> Messages { get; set; } = [];

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public static (Guid Low, Guid High) Pair(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
}

public sealed class DirectMessage : BaseEntity
{
    public Guid ThreadId { get; set; }
    public DirectThread? Thread { get; set; }

    public Guid SenderId { get; set; }
    public required string SenderName { get; set; }
    public required string Body { get; set; }

    public DateTime? SeenAtUtc { get; set; }
}

/// <summary>
/// One person refusing contact from another. Directional on purpose: blocking is a decision
/// about who may reach you, not an agreement between two people.
/// </summary>
public sealed class UserBlock : BaseEntity
{
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// A message reported to the Security desk. The text is copied in at the moment of reporting,
/// because the sender can delete their account and the desk still needs to see what was said.
/// </summary>
public sealed class MessageReport : BaseEntity
{
    public Guid ReporterId { get; set; }
    public required string ReporterEmail { get; set; }

    public Guid AboutUserId { get; set; }
    public required string AboutEmail { get; set; }

    public required string Quote { get; set; }
    public string? Reason { get; set; }

    public bool Handled { get; set; }
    public DateTime? HandledAtUtc { get; set; }
}
