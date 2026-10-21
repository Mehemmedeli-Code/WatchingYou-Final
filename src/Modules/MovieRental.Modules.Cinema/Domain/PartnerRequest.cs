using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

public enum PartnerKind { Female = 1, Male = 2, Mixed = 3 }

/// <summary>
/// SearchingYou: someone with a ticket looking for company at that screening. Open until the
/// film starts, until enough people have joined, or until its owner closes it.
/// </summary>
public sealed class PartnerRequest : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ScreeningId { get; set; }
    public Screening? Screening { get; set; }

    public PartnerKind Partner { get; set; }
    public int PeopleWanted { get; set; }

    /// <summary>Tickets already bought (the owner holds one per person), or to be bought: then the
    /// search lives only while the screening still has a seat for everyone in the group.</summary>
    public bool HasTickets { get; set; }
    /// <summary>Closed by its owner.</summary>
    public DateTime? ClosedAtUtc { get; set; }
    /// <summary>The last place was taken: found. It stays visible to the owner and the people who
    /// joined until the film starts.</summary>
    public DateTime? FilledAtUtc { get; set; }
    /// <summary>Called off by the site: too few seats were left for the group. "No tickets were
    /// bought, the meet-up is off" — shown to the owner and to everyone who joined.</summary>
    public DateTime? CancelledAtUtc { get; set; }

    public List<PartnerRequestMember> Members { get; set; } = [];
}

public sealed class PartnerRequestMember : BaseEntity
{
    public Guid RequestId { get; set; }
    public Guid UserId { get; set; }
}
