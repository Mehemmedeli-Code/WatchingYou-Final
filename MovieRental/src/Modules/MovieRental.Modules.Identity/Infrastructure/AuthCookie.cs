using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using MovieRental.Modules.Identity.Domain;

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

    public static Task SignInAsync(HttpContext? context, AppUser user)
    {
        if (context is null) return Task.CompletedTask;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email)
        };
        claims.AddRange(user.RoleList.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return context.SignInAsync(SchemeName, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
    }

    public static Task SignOutAsync(HttpContext? context) =>
        context is null ? Task.CompletedTask : context.SignOutAsync(SchemeName);
}
