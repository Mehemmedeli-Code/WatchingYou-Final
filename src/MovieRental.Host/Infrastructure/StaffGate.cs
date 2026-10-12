using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MovieRental.Host.Pages;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// Where the staff screens live. Not /admin: they sit under a long secret segment
/// (Staff:PathKey), so somebody typing /admin or /security finds nothing at all. The path is
/// only a first fence — the roles and the staff PIN are what actually guard the screens.
/// </summary>
public static partial class StaffPaths
{
    [GeneratedRegex("^[A-Za-z0-9-]{24,128}$")]
    private static partial Regex KeyPattern();

    public static string Base { get; private set; } = "/staff-not-configured";

    public static void Configure(IConfiguration configuration)
    {
        var key = configuration["Staff:PathKey"];
        if (key is null || !KeyPattern().IsMatch(key))
            throw new InvalidOperationException(
                "Staff:PathKey must be 24+ letters, digits or dashes. Set it in user secrets, never in appsettings.json.");
        Base = "/" + key;
    }

    public static string Admin => Base + "/admin";
    public static string Security => Base + "/security";
    public static string BackOffice => Base + "/backoffice";
    public static string Unlock => Base + "/unlock";
}

/// <summary>
/// "This browser passed the staff PIN, for this account, a little while ago": an encrypted,
/// time-limited, HttpOnly cookie. Bound to the user id, so signing in as someone else in the
/// same browser does not inherit it.
/// </summary>
public static class StaffUnlock
{
    public const string CookieName = "wy.staff";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(4);
    private const string Purpose = "WatchingYou.StaffUnlock.v1";

    private static ITimeLimitedDataProtector Protector(HttpContext http) =>
        http.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).ToTimeLimitedDataProtector();

    private static string? UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    public static void Issue(HttpContext http)
    {
        var id = UserId(http.User) ?? throw new InvalidOperationException("Not signed in.");
        http.Response.Cookies.Append(CookieName, Protector(http).Protect(id, Lifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = Lifetime,
            IsEssential = true
        });
    }

    public static void Clear(HttpContext http) => http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });

    public static bool IsUnlocked(HttpContext http)
    {
        var id = UserId(http.User);
        if (id is null || !http.Request.Cookies.TryGetValue(CookieName, out var value)) return false;
        try { return Protector(http).Unprotect(value) == id; }
        catch (System.Security.Cryptography.CryptographicException) { return false; }   // expired, tampered or old key
    }
}

/// <summary>Added to every staff policy, so the PIN guards the API as well as the pages: a
/// stolen session token calling /api/admin directly is refused just the same.</summary>
public sealed class StaffUnlockedRequirement : IAuthorizationRequirement;

internal sealed class StaffUnlockedHandler(IHttpContextAccessor accessor) : AuthorizationHandler<StaffUnlockedRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, StaffUnlockedRequirement requirement)
    {
        if (accessor.HttpContext is { } http && StaffUnlock.IsUnlocked(http)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A staff screen. Anyone without one of its roles gets the ordinary "nothing here" page —
/// not a sign-in prompt, which would confirm that something is here. Staff who have not
/// passed the PIN in this browser are sent to the PIN page first.
/// </summary>
public abstract class StaffPageModel : AppPageModel
{
    protected abstract string[] AllowedRoles { get; }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true || !AllowedRoles.Any(http.User.IsInRole))
        {
            context.Result = NotFound();
            return;
        }
        if (!StaffUnlock.IsUnlocked(http))
        {
            var back = http.Request.Path + http.Request.QueryString;
            context.Result = Redirect(StaffPaths.Unlock + "?returnUrl=" + Uri.EscapeDataString(back));
            return;
        }
        await next();
    }
}
