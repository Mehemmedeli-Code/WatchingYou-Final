using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

public sealed class Screening : BaseEntity, ISoftDeletable
{
    public Guid MovieId { get; set; }
    public required string MovieTitle { get; set; }
    /// <summary>The room this performance plays in. Rows and SeatsPerRow below are copied
    /// from it when the screening is created, so re-fitting a hall later never silently
    /// invalidates seats already sold.</summary>
    public Guid HallId { get; set; }
    public Hall? HallRoom { get; set; }

    public required string Hall { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public int Rows { get; set; } = 8;
    public int SeatsPerRow { get; set; } = 12;
    public decimal SeatPrice { get; set; } = 9.00m;

    /// <summary>Two-letter code — az, en, ru or tr. A screening is a specific performance in
    /// a specific language, so the language belongs here and not on the film.</summary>
    public string AudioLanguage { get; set; } = "az";
    public string? SubtitleLanguage { get; set; }

    /// <summary>Called off by the cinema. Every booking on it becomes fully refundable, and no
    /// cut-off applies — the customer did not change their mind.</summary>
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }

    // --- refund rules, overriding the site defaults for this performance only ---
    /// <summary>Hours before the start up to which a refund is possible. Null uses 48.</summary>
    public int? RefundWindowHours { get; set; }
    /// <summary>Percentage kept on a refund inside that window. Null uses 30.</summary>
    public decimal? RefundFeePercent { get; set; }
    /// <summary>Anything unusual about this screening, shown in its Rules section.</summary>
    public string? RefundNote { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public List<SeatBooking> Bookings { get; set; } = [];
    public int Capacity => Rows * SeatsPerRow;
}

public sealed class SeatBooking : BaseEntity, ISoftDeletable
{
    public Guid ScreeningId { get; set; }
    public Screening? Screening { get; set; }

    public Guid UserId { get; set; }
    public int Row { get; set; }
    public int Number { get; set; }
    public decimal PricePaid { get; set; }

    /// <summary>The checkout this seat belongs to. Rows are written at payment time, not at
    /// confirmation, so the unique index on (screening, row, number) holds the seat against
    /// everyone else for as long as the code is valid.</summary>
    public Guid PaymentId { get; set; }
    public SeatPayment? Payment { get; set; }

    /// <summary>Null while the e-mailed code is still outstanding.</summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>Set when the seat is scanned at the door. A ticket that admits twice is not
    /// a ticket, so this is what makes a second scan report "already used".</summary>
    public DateTime? CheckedInAtUtc { get; set; }
    public Guid? CheckedInByUserId { get; set; }
    public bool IsConfirmed => ConfirmedAtUtc is not null;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
