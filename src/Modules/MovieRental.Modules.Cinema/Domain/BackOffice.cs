using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

// The back office — what a cinema runs on behind the counter. Restaurants have R-Keeper;
// cinemas have a theatre management system (Vista, Veezi, UCS Premiera) doing the same jobs:
// a till for tickets, a till for the bar, cash shifts with X and Z reports, ticket types,
// and the settlement owed to each film's distributor. These are the entities for those jobs.

public enum TenderType { Cash = 1, Card = 2 }

/// <summary>
/// A price band sold at the box office — Adult, Child, Student. Stored as a percentage of the
/// screening's price rather than an amount, so one screening priced at 12 AZN and another at
/// 8 AZN both get the right child discount without a table of prices per performance.
/// </summary>
public sealed class TicketType : BaseEntity, ISoftDeletable
{
    /// <summary>The stable name — English, unique. It is what a sold seat snapshots and what
    /// reports group by, so renaming a translation never splits one tariff into two rows.</summary>
    public required string Name { get; set; }

    /// <summary>What the till and the reports show in each interface language. Empty falls
    /// back to <see cref="Name"/>, so a tariff typed in one language still works in all four.</summary>
    public string? NameAz { get; set; }
    public string? NameRu { get; set; }
    public string? NameTr { get; set; }

    public decimal PercentOfBase { get; set; } = 100m;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public decimal PriceFor(decimal basePrice) => Math.Round(basePrice * PercentOfBase / 100m, 2);
}

public enum ConcessionCategory { Popcorn = 1, Drinks = 2, Snacks = 3, Combo = 4 }

/// <summary>Something sold at the bar. Stock is counted in units and decremented with a
/// conditional UPDATE, so two tills cannot both sell the last cola.</summary>
public sealed class ConcessionItem : BaseEntity, ISoftDeletable
{
    public required string Name { get; set; }
    public ConcessionCategory Category { get; set; } = ConcessionCategory.Snacks;
    public decimal Price { get; set; }
    /// <summary>What the cinema pays for it. Price minus this is the margin the report shows.</summary>
    public decimal CostPrice { get; set; }
    public int Stock { get; set; }
    public int LowStockThreshold { get; set; } = 10;
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

/// <summary>One bar receipt.</summary>
public sealed class ConcessionSale : BaseEntity
{
    public Guid ShiftId { get; set; }
    public required string Reference { get; set; }
    public TenderType Tender { get; set; }
    public decimal Total { get; set; }
    public decimal Cost { get; set; }
    public List<ConcessionSaleLine> Lines { get; set; } = [];
}

/// <summary>Name and prices are snapshotted: a receipt must still read correctly after the
/// menu changes.</summary>
public sealed class ConcessionSaleLine : BaseEntity
{
    public Guid SaleId { get; set; }
    public Guid ItemId { get; set; }
    public required string Name { get; set; }
    public ConcessionCategory Category { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// A cashier's shift at a till, from opening float to the counted drawer.
///
/// Closing is the Z report: expected cash is worked out from the sales, the cashier types what
/// is actually in the drawer, and the difference is stored — never recomputed, because the
/// point of a Z report is that it is final. One open shift per cashier at a time.
/// </summary>
public sealed class CashShift : BaseEntity
{
    public Guid CashierId { get; set; }
    public required string CashierName { get; set; }
    public decimal OpeningFloat { get; set; }

    public DateTime? ClosedAtUtc { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string? Note { get; set; }

    public bool IsOpen => ClosedAtUtc is null;
}

/// <summary>
/// The terms a film is played on. Cinemas do not own the films they show: the distributor
/// takes an agreed share of the box office — often 50–60% in the opening weeks. The settlement
/// report multiplies the net ticket revenue by this share to say what is owed.
/// </summary>
public sealed class FilmDeal : BaseEntity, ISoftDeletable
{
    public Guid MovieId { get; set; }
    public required string MovieTitle { get; set; }
    public required string Distributor { get; set; }
    public decimal SharePercent { get; set; } = 50m;
    public string? Note { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
