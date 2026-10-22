using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

public enum LoyaltyReason
{
    /// <summary>Points for a confirmed booking.</summary>
    Earned = 1,
    /// <summary>Points spent on a booking.</summary>
    Redeemed = 2,
    /// <summary>Earned points taken back because the booking was refunded.</summary>
    Reversed = 3,
    /// <summary>Spent points given back because the booking was refunded.</summary>
    Restored = 4,
    /// <summary>A manual correction by an admin.</summary>
    Adjusted = 5
}

/// <summary>
/// One line in a customer's points ledger. The balance is the sum of the lines; nothing stores
/// a running total that could drift from them. Append-only for the same reason the audit log
/// is: a refund does not delete what was earned, it adds a line that takes it back.
/// </summary>
public sealed class LoyaltyEntry : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Positive for points in, negative for points out.</summary>
    public int Points { get; set; }
    public LoyaltyReason Reason { get; set; }

    public Guid? PaymentId { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
}
