using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

public enum VerificationChannel { Email = 1, Sms = 2 }

/// <summary>
/// A one-time code. Only a salted hash is stored: a six-digit code is short enough that
/// anyone reading the table would otherwise be able to use it directly.
/// </summary>
public sealed class VerificationCode : BaseEntity
{
    public const int MaxAttempts = 5;

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public VerificationChannel Channel { get; set; }
    public required string CodeHash { get; set; }
    public required string Salt { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int Attempts { get; set; }

    public bool IsUsable => ConsumedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc && Attempts < MaxAttempts;
    public int AttemptsLeft => Math.Max(0, MaxAttempts - Attempts);
}
