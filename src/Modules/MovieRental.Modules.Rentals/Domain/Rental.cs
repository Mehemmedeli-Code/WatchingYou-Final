using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Rentals.Domain;

public enum RentalStatus { Active = 1, Returned = 2, Overdue = 3 }

public sealed class Rental : BaseEntity, ISoftDeletable
{
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }

    /// <summary>Title copied at rental time. A rental receipt has to keep reading correctly
    /// even after an admin renames or retires the title.</summary>
    public required string MovieTitle { get; set; }
    public string? PosterUrl { get; set; }

    public DateTime RentedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DueAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public int ExtensionCount { get; set; }

    public decimal DailyPrice { get; set; }
    public decimal BasePrice { get; set; }
    public decimal LateFee { get; set; }

    public bool DueSoonNotified { get; set; }
    public bool OverdueNotified { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public RentalStatus StatusAt(DateTime nowUtc) =>
        ReturnedAtUtc is not null ? RentalStatus.Returned
        : nowUtc > DueAtUtc ? RentalStatus.Overdue
        : RentalStatus.Active;

    public int DaysOverdue(DateTime nowUtc) =>
        nowUtc <= DueAtUtc ? 0 : (int)Math.Ceiling((nowUtc - DueAtUtc).TotalDays);
}
