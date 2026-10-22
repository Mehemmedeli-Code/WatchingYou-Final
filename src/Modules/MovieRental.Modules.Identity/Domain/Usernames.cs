using System.Text.RegularExpressions;

namespace MovieRental.Modules.Identity.Domain;

/// <summary>The rules for a public handle, in one place: registration, the profile page and
/// the People search all go through here.</summary>
public static partial class Usernames
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    [GeneratedRegex("^[a-z0-9._]{3,30}$")]
    private static partial Regex Allowed();

    /// <summary>Lower-cased and trimmed, with a leading @ dropped: what people type in a search box.</summary>
    public static string Normalize(string? value) => (value ?? "").Trim().TrimStart('@').ToLowerInvariant();

    public static bool IsValid(string normalized) => Allowed().IsMatch(normalized);

    /// <summary>Handles that would pass for the site or its staff. Nobody gets them, so a
    /// message from "@support" can only ever be a stranger impersonating it.</summary>
    private static readonly HashSet<string> Reserved =
    [
        "admin", "administrator", "root", "support", "help", "helpdesk", "security", "staff", "moderator", "mod",
        "official", "system", "watchingyou", "watching_you", "watching.you", "cashier", "kassa", "api", "www",
    ];

    public static bool IsReserved(string normalized) =>
        Reserved.Contains(normalized) || Reserved.Contains(normalized.Replace(".", "").Replace("_", ""));

    /// <summary>A starting handle from the e-mail's local part, e.g. "nigar.a+kino" becomes "nigar.a".</summary>
    public static string FromEmail(string email)
    {
        var local = email.Split('@')[0].Split('+')[0].ToLowerInvariant();
        var cleaned = new string(local.Where(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_').ToArray());
        if (cleaned.Length < MinLength || IsReserved(cleaned)) cleaned = "user" + cleaned;
        return cleaned.Length > MaxLength - 5 ? cleaned[..(MaxLength - 5)] : cleaned;
    }
}
