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

    /// <summary>Works the box office and the bar: sells tickets and concessions inside an
    /// open cash shift. Sees the back office's point-of-sale screens and nothing else of it —
    /// prices, menus and reports belong to the manager.</summary>
    public const string Cashier = "Cashier";

    /// <summary>The site's owner. Exactly one account holds it, named in configuration
    /// (Staff:OwnerEmail) and given it at start-up; it can never be handed out, taken away,
    /// suspended or deleted through the site. Only the owner promotes or demotes admins and
    /// sets the staff PINs. The owner also holds Admin, so every admin screen works for them.</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Looks after people, not the business: answers help requests and handles
    /// reported messages. No films, prices, money or roles.</summary>
    public const string Moderator = "Moderator";

    /// <summary>Every role that opens a staff screen, and so has to pass the staff PIN.</summary>
    public static readonly string[] Staff = [SuperAdmin, Admin, Security, Cashier, Moderator];
}

public static class AppPolicies
{
    /// <summary>Security reviewers plus admins. Named separately from the roles so the
    /// "admin can see everything" rule lives in one place instead of every endpoint.</summary>
    public const string SecurityDesk = "SecurityDesk";

    /// <summary>Cashiers plus admins: whoever may stand at a till in the back office.</summary>
    public const string BackOffice = "BackOffice";

    /// <summary>Moderators, security reviewers and admins: help requests and reported messages.</summary>
    public const string Moderation = "Moderation";

    /// <summary>Rate-limit policy names. Declared here, configured by the host, requested by
    /// the slices that need them — guessing attacks are aimed at endpoints, not at modules.</summary>
    public const string AuthRateLimit = "auth";
    public const string CodeRateLimit = "codes";

    /// <summary>Things one person writes that land on someone else's screen or disk: messages,
    /// follows, blocks, reports, comments, uploads. Per account, so one member cannot flood another.</summary>
    public const string WriteRateLimit = "writes";

    /// <summary>Output-cache policies for public reads. Named here so modules can mark an
    /// endpoint cacheable without knowing how the host caches it.</summary>
    public const string CatalogueCache = "cache-catalogue";
    public const string CinemaCache = "cache-cinema";
    public const string VenueCache = "cache-venues";
}

public static class CacheTags
{
    public const string Catalogue = "catalogue";
    public const string Cinema = "cinema";
}
