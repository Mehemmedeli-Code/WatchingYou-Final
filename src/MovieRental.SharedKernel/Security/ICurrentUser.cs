namespace MovieRental.SharedKernel.Security;

public interface ICurrentUser
{
    Guid? Id { get; }
    string? Email { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    Guid RequireId();
}

public static class AppRoles
{
    public const string Admin = "Admin";

    /// <summary>Reviews uploaded films against the safety checklist. Cannot approve —
    /// separating the person who inspects from the person who publishes is the point.</summary>
    public const string Security = "Security";

    public const string Customer = "Customer";
}

public static class AppPolicies
{
    /// <summary>Security reviewers plus admins. Named separately from the roles so the
    /// "admin can see everything" rule lives in one place instead of every endpoint.</summary>
    public const string SecurityDesk = "SecurityDesk";

    /// <summary>Rate-limit policy names. Declared here, configured by the host, requested by
    /// the slices that need them — guessing attacks are aimed at endpoints, not at modules.</summary>
    public const string AuthRateLimit = "auth";
    public const string CodeRateLimit = "codes";
}
