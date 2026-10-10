using Microsoft.AspNetCore.Http;

namespace MovieRental.Modules.Identity.Infrastructure;

/// <summary>
/// Where the website keeps its refresh token: an HttpOnly cookie that page scripts cannot
/// read, sent only to /api/auth and only from this site. The website asks for this with the
/// X-Refresh-Mode: cookie header; the phone app does not, and keeps getting the token in the
/// answer as before.
///
/// One cookie per account (the name carries the user id), because a browser can have two
/// accounts signed in at once in two tabs — a customer and the help desk, say.
/// </summary>
public static class RefreshCookie
{
    public const string ModeHeader = "X-Refresh-Mode";

    private static string Name(Guid userId) => $"rr.rt.{userId:N}";

    private static CookieOptions Options(DateTime? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        IsEssential = true,
        Expires = expires
    };

    public static bool Wanted(HttpContext? http) =>
        http is not null && string.Equals(http.Request.Headers[ModeHeader], "cookie", StringComparison.OrdinalIgnoreCase);

    /// <summary>Hands the token out: into the cookie for the website (and an empty string for
    /// the answer), or back as it is for the phone app.</summary>
    public static string Issue(HttpContext? http, Guid userId, string token, DateTime expiresAtUtc)
    {
        if (!Wanted(http)) return token;
        http!.Response.Cookies.Append(Name(userId), token, Options(expiresAtUtc));
        return string.Empty;
    }

    public static string? Read(HttpContext? http, Guid? userId) =>
        http is not null && userId is { } id ? http.Request.Cookies[Name(id)] : null;

    public static void Delete(HttpContext? http, Guid? userId)
    {
        if (http is not null && userId is { } id) http.Response.Cookies.Delete(Name(id), Options(null));
    }
}
