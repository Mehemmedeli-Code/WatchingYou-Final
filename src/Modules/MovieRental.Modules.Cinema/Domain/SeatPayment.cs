using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

public enum PaymentStatus { AwaitingCode = 1, Confirmed = 2, Expired = 3, Cancelled = 4, Refunded = 5 }

public enum CardBrand { Unknown = 0, Visa = 1, Mastercard = 2 }

/// <summary>How the money was taken. Card is the built-in test checkout with the e-mailed code;
/// Stripe is Stripe Checkout in test mode, used when a Stripe key is configured.</summary>
public enum PaymentProvider { Card = 1, Stripe = 2, BoxOffice = 3 }

/// <summary>
/// One checkout. Seats are held from the moment this is created and only become a real
/// ticket once the code sent by e-mail comes back.
///
/// This is a teaching implementation: no acquirer is contacted and no money moves. What it
/// does do correctly is refuse to keep the card. The number is validated, its brand and
/// last four digits are kept for the receipt, and the rest is discarded before anything is
/// written — a full PAN or a CVC in a database is a liability, not a feature.
/// </summary>
public sealed class SeatPayment : BaseEntity
{
    public const int MaxAttempts = 5;

    public Guid ScreeningId { get; set; }
    public Screening? Screening { get; set; }

    public Guid UserId { get; set; }
    public required string Reference { get; set; }

    /// <summary>What was charged — after the promo code and any points.</summary>
    public decimal Amount { get; set; }

    /// <summary>Seats times price, before any discount.</summary>
    public decimal Subtotal { get; set; }
    public string? PromoCode { get; set; }
    public decimal PromoDiscount { get; set; }
    public int PointsRedeemed { get; set; }
    public decimal PointsDiscount { get; set; }
    /// <summary>Set when the booking is confirmed; taken back again if it is refunded.</summary>
    public int PointsEarned { get; set; }

    public PaymentProvider Provider { get; set; } = PaymentProvider.Card;

    /// <summary>Box office only: cash or card at the counter, and the till shift that took
    /// it. Online sales have neither — they never pass through a drawer.</summary>
    public TenderType? Tender { get; set; }
    public Guid? ShiftId { get; set; }
    /// <summary>Stripe's Checkout Session id, to match the return trip to this booking.</summary>
    public string? ExternalSessionId { get; set; }
    /// <summary>Stripe's PaymentIntent id, which a refund has to name.</summary>
    public string? ExternalPaymentId { get; set; }
    public CardBrand Brand { get; set; }
    public required string Last4 { get; set; }
    public required string CardHolder { get; set; }

    public required string CodeHash { get; set; }
    public required string Salt { get; set; }
    public int Attempts { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingCode;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(15);
    public DateTime? ConfirmedAtUtc { get; set; }

    public DateTime? RefundedAtUtc { get; set; }
    /// <summary>What went back, after any fee. Stored rather than recomputed: the policy may
    /// change later, and a receipt must still say what was actually paid back.</summary>
    public decimal? RefundedAmount { get; set; }
    public string? RefundReason { get; set; }

    public List<SeatBooking> Seats { get; set; } = [];

    public bool IsUsable =>
        Status == PaymentStatus.AwaitingCode && DateTime.UtcNow < ExpiresAtUtc && Attempts < MaxAttempts;

    public int AttemptsLeft => Math.Max(0, MaxAttempts - Attempts);
}
