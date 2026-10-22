using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Persistence;

namespace MovieRental.Modules.Identity.Infrastructure;

/// <summary>
/// The API runs on bearer tokens, but Razor renders before any JavaScript has run, and a
/// browser navigating to /swagger sends no Authorization header. A parallel HttpOnly cookie
/// gives the server side an identity it can trust for those two jobs — deciding which nav
/// items to render, and gating the API reference — without weakening the token flow.
/// </summary>
public static class AuthCookie
{
    public const string SchemeName = CookieAuthenticationDefaults.AuthenticationScheme;
    private const string StampClaim = "rr.stamp";

    public static Task SignInAsync(HttpContext? context, AppUser user)
    {
        if (context is null) return Task.CompletedTask;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(StampClaim, StampOf(user))
        };
        claims.AddRange(user.RoleList.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return context.SignInAsync(SchemeName, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
    }

    public static Task SignOutAsync(HttpContext? context) =>
        context is null ? Task.CompletedTask : context.SignOutAsync(SchemeName);

    /// <summary>
    /// Wired to the cookie's OnValidatePrincipal. The cookie lives 14 days and slides, and it
    /// carries roles, so without this a suspended, deleted or demoted user — or an attacker
    /// holding the cookie after the victim reset their password — kept full API access. The
    /// stamp changes whenever the password or the roles do; one primary-key lookup per request.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        // Cookies issued before the stamp existed land here too; they just sign in again.
        if (await IsCurrentAsync(context.HttpContext, context.Principal)) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(SchemeName);
    }

    /// <summary>
    /// The same check for a bearer token (wired to JwtBearer's OnTokenValidated). Without it a
    /// suspended or deleted account, or one whose password or roles changed, kept a working
    /// access token until it expired — up to fifteen minutes. A rejected token makes the
    /// client refresh, and the refresh refuses an account that is no longer allowed in.
    /// </summary>
    public static async Task<bool> IsCurrentAsync(HttpContext http, ClaimsPrincipal? principal)
    {
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal?.FindFirstValue(StampClaim);
        if (!Guid.TryParse(id, out var userId) || stamp is null) return false;

        var db = http.RequestServices.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user is { IsDeleted: false, IsSuspended: false } && StampOf(user) == stamp;
    }

    internal const string StampClaimType = StampClaim;

    internal static string StampOf(AppUser user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.PasswordHash}|{user.Roles}")))[..16];
}
