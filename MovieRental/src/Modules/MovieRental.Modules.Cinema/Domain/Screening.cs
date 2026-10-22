using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

public sealed class Screening : BaseEntity, ISoftDeletable
{
    public Guid MovieId { get; set; }
    public required string MovieTitle { get; set; }
    public required string Hall { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public int Rows { get; set; } = 8;
    public int SeatsPerRow { get; set; } = 12;
    public decimal SeatPrice { get; set; } = 9.00m;

    /// <summary>Two-letter code — az, en, ru or tr. A screening is a specific performance in
    /// a specific language, so the language belongs here and not on the film.</summary>
    public string AudioLanguage { get; set; } = "az";
    public string? SubtitleLanguage { get; set; }

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

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
