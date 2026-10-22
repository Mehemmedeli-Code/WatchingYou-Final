using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

/// <summary>
/// One row per issued refresh token. Rotation means a used token is revoked and points
/// at its replacement, so a replayed token exposes the whole chain and lets us revoke it.
/// </summary>
public sealed class RefreshTokenEntity : BaseEntity
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public required string Token { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsRevoked => RevokedAtUtc is not null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
