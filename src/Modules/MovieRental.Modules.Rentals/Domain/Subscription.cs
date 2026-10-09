using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Rentals.Domain;

/// <summary>
/// One paid month of Watching PRO. Each payment is its own row, so the history reads like a
/// statement; a renewal paid before the month runs out starts where the previous one ends,
/// so nobody loses days by paying early.
///
/// There is no automatic renewal: the card is never stored (only its brand and last four
/// digits, for the receipt), so nothing can be charged without the member paying again.
/// </summary>
public sealed class Subscription : BaseEntity
{
    public Guid UserId { get; set; }
    public required string Plan { get; set; }

    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }

    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string CardBrand { get; set; }
    public required string CardLast4 { get; set; }

    /// <summary>When the "ends in 3 days" e-mail went out for this month. Set on the latest
    /// paid month only; a renewal is a new row, so it gets its own reminder in turn.</summary>
    public DateTime? ReminderSentAtUtc { get; set; }

    public bool IsActiveAt(DateTime nowUtc) => StartsAtUtc <= nowUtc && nowUtc < EndsAtUtc;
}
