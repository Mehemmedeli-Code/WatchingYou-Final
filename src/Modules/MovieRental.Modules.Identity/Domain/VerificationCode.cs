using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

/// <summary>
/// What the code unlocks. Without it, a code issued to
/// confirm an e-mail address would also reset the password behind it, which turns a
/// harmless "verify me" mail into an account takeover.
/// </summary>
public enum VerificationPurpose { AccountVerification = 1, PasswordReset = 2 }

/// <summary>
/// A one-time code, always sent by e-mail. Only a salted hash is stored: a six-digit code is short enough that
/// anyone reading the table would otherwise be able to use it directly.
/// </summary>
public sealed class VerificationCode : BaseEntity
{
    public const int MaxAttempts = 5;

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public VerificationPurpose Purpose { get; set; } = VerificationPurpose.AccountVerification;
    public required string CodeHash { get; set; }
    public required string Salt { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int Attempts { get; set; }

    public bool IsUsable => ConsumedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc && Attempts < MaxAttempts;
    public int AttemptsLeft => Math.Max(0, MaxAttempts - Attempts);
}
