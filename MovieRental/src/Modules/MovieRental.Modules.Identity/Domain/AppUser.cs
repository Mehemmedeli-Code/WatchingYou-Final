using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

public sealed class AppUser : BaseEntity, ISoftDeletable
{
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsEmailConfirmed { get; set; }
    public bool IsPhoneConfirmed { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Comma separated role list. A join table would be over-engineering for a
    /// two-role system; promote it to its own entity the day roles gain metadata.</summary>
    public string Roles { get; set; } = SharedKernel.Security.AppRoles.Customer;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public List<RefreshTokenEntity> RefreshTokens { get; set; } = [];
    public List<VerificationCode> VerificationCodes { get; set; } = [];

    public string[] RoleList => Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
