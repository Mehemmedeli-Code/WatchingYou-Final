using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

public sealed class AppUser : BaseEntity, ISoftDeletable
{
    public required string Email { get; set; }
    public required string FullName { get; set; }
    /// <summary>Public handle, unique, shown as @name and searched on the People page. Lower case
    /// letters, digits, dots and underscores; set at registration, changeable on the profile.</summary>
    public string? Username { get; set; }

    /// <summary>A private account approves its followers: following it sends a request, and its
    /// follower lists are shown only to people it has accepted.</summary>
    public bool IsPrivate { get; set; }

    /// <summary>A few lines under the name on the profile, up to 150 characters.</summary>
    public string? Bio { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsEmailConfirmed { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Suspension rather than deletion: the rentals, reviews and bookings behind an
    /// account still have to make sense after the person is barred.</summary>
    /// <summary>
    /// Globe presence. Off unless the person turns it on, and city-level only.
    ///
    /// Publishing where somebody lives is not a detail to get wrong. Nobody is put on the map
    /// by registering: appearing there is a choice, it is reversible, and the coordinates are
    /// the city's, never the person's — the browser is never asked for a precise position.
    /// </summary>
    public bool ShareOnGlobe { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? AvatarUrl { get; set; }

    public bool IsSuspended { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }
    public string? SuspensionReason { get; set; }

    /// <summary>Comma separated role list. A join table would be over-engineering for a
    /// two-role system; promote it to its own entity the day roles gain metadata.</summary>
    public string Roles { get; set; } = SharedKernel.Security.AppRoles.Customer;

    /// <summary>The staff PIN, hashed like a password: asked again on the way into any staff
    /// screen, so a stolen session alone does not open the admin panel. Set by the owner.</summary>
    public string? StaffPinHash { get; set; }
    public int StaffPinFailures { get; set; }
    public DateTime? StaffPinLockedUntilUtc { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public List<RefreshTokenEntity> RefreshTokens { get; set; } = [];
    public List<VerificationCode> VerificationCodes { get; set; } = [];

    public string[] RoleList => Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// What a closed account leaves behind: a row the bookings and payments can still point at,
    /// saying nothing about who it was, with every session ended. The caller soft-deletes it.
    /// </summary>
    /// <param name="unusablePasswordHash">The hash of a random secret nobody holds.</param>
    public void Anonymise(string unusablePasswordHash, DateTime nowUtc, string reason)
    {
        Email = $"deleted-{Id:N}@deleted.invalid";
        FullName = "Deleted user";
        Username = null;
        Bio = null;
        PasswordHash = unusablePasswordHash;
        ShareOnGlobe = false;
        City = null;
        CountryCode = null;
        Latitude = null;
        Longitude = null;
        AvatarUrl = null;

        foreach (var token in RefreshTokens.Where(t => t.RevokedAtUtc == null))
        {
            token.RevokedAtUtc = nowUtc;
            token.RevokedReason = reason;
        }
    }
}
