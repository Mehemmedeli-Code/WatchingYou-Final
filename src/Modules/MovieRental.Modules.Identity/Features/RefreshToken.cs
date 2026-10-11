using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

/// <param name="RefreshToken">The phone app sends the token itself.</param>
/// <param name="UserId">The website sends only whose session this is; the token is in that
/// account's HttpOnly cookie (<see cref="RefreshCookie"/>).</param>
public sealed record RefreshTokenCommand(string? RefreshToken, Guid? UserId = null) : ICommand<Result<AuthResponse>>;

internal sealed class RefreshTokenHandler(
    IdentityDbContext db, ITokenService tokens, IHttpContextAccessor http)
    : ICommandHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var fromCookie = string.IsNullOrEmpty(command.RefreshToken);
        var presented = fromCookie ? RefreshCookie.Read(http.HttpContext, command.UserId) : command.RefreshToken;
        if (string.IsNullOrEmpty(presented))
            return Result.Failure<AuthResponse>(Error.Unauthorized("Unknown refresh token."));

        var hash = TokenService.HashRefreshToken(presented);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == hash, ct);

        // A cookie is read by the account named in the request; it must belong to that account.
        if (stored?.User is null || (fromCookie && stored.UserId != command.UserId))
            return Result.Failure<AuthResponse>(Error.Unauthorized("Unknown refresh token."));

        // Replay detection: a token that was already rotated should never come back.
        // When it does, the chain is compromised, so every live token for the user dies.
        //
        // With one exception, a few seconds long. The browser can lose the answer to a refresh
        // it sent: the user clicks a link while the page is loading, the tab navigates, and the
        // new token never gets stored. The next page then presents the old one — not theft, just
        // a navigation — and treating it as theft signed people out of every device for clicking
        // too fast. A token rotated moments ago whose replacement is still unused is therefore
        // honoured by rotating that replacement instead. Anything older is still a replay.
        if (stored.IsRevoked)
        {
            var successor = await RecentSuccessorAsync(stored, ct);
            if (successor is null)
            {
                await RevokeDescendantsAsync(stored, ct);
                await db.SaveChangesAsync(ct);
                return Result.Failure<AuthResponse>(Error.Unauthorized("This session was ended. Sign in again."));
            }
            stored = successor;
        }

        if (!stored.IsActive)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Refresh token has expired."));

        var ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var (replacement, replacementValue) = tokens.CreateRefreshToken(stored.UserId, ip);

        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.RevokedReason = "rotated";
        stored.ReplacedByToken = replacement.Token;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);

        // A successor token may come back without its user loaded; fetch it rather than assume.
        var user = stored.User ?? await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);
        if (user is null)
            return Result.Failure<AuthResponse>(Error.Unauthorized("This session was ended. Sign in again."));

        // Re-issue the page cookie with the current roles and stamp, so a cookie that
        // AuthCookie.ValidateAsync rejected (role change, or issued before stamps) recovers on
        // the next refresh instead of leaving pages signed out while the API is signed in.
        if (!user.IsSuspended) await AuthCookie.SignInAsync(http.HttpContext, user);

        var access = tokens.CreateAccessToken(user);
        var handedOut = RefreshCookie.Issue(http.HttpContext, user.Id, replacementValue, replacement.ExpiresAtUtc);
        return Result.Success(new AuthResponse(access.Value, access.ExpiresAtUtc, handedOut, user.ToProfile()));
    }

    /// <summary>How long a just-rotated token stays usable, to cover an answer the browser lost.</summary>
    private static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    private async Task<Domain.RefreshTokenEntity?> RecentSuccessorAsync(Domain.RefreshTokenEntity revoked, CancellationToken ct)
    {
        if (revoked.RevokedReason != "rotated" || revoked.ReplacedByToken is null) return null;
        if (revoked.RevokedAtUtc is not { } at || DateTime.UtcNow - at > ReuseGrace) return null;

        var successor = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == revoked.ReplacedByToken, ct);

        return successor is { IsActive: true, User: not null } ? successor : null;
    }

    private async Task RevokeDescendantsAsync(Domain.RefreshTokenEntity compromised, CancellationToken ct)
    {
        var live = await db.RefreshTokens
            .Where(t => t.UserId == compromised.UserId && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in live)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedReason = "reuse detected";
        }
    }
}

public sealed record RevokeTokenCommand(string? RefreshToken, Guid? UserId = null) : ICommand<Result>;

internal sealed class RevokeTokenHandler(IdentityDbContext db, IHttpContextAccessor http) : ICommandHandler<RevokeTokenCommand, Result>
{
    public async Task<Result> Handle(RevokeTokenCommand command, CancellationToken ct)
    {
        var presented = string.IsNullOrEmpty(command.RefreshToken)
            ? RefreshCookie.Read(http.HttpContext, command.UserId)
            : command.RefreshToken;
        RefreshCookie.Delete(http.HttpContext, command.UserId);
        if (string.IsNullOrEmpty(presented)) return Result.Failure(Error.NotFound("Refresh token"));

        var hash = TokenService.HashRefreshToken(presented);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == hash, ct);
        if (stored is null) return Result.Failure(Error.NotFound("Refresh token"));

        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.RevokedReason = "signed out";
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class RefreshTokenEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/refresh",
            async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> (
                RefreshTokenCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Unauthorized();
            })
        .WithName("RefreshToken").WithTags("Auth").AllowAnonymous();

        // The page cookie and the app's refresh token are two halves of one sign-in. When the
        // token is gone (expired, revoked, another tab's replay) while the cookie is still good,
        // the header said "signed in" and every page said "sign in". This mints a fresh token
        // pair from the cookie instead. Cookie only, and the cookie is re-validated on every
        // request (AuthCookie.ValidateAsync); SameSite=Lax keeps it off cross-site POSTs and
        // CORS keeps other sites from reading the answer.
        app.MapPost("/api/auth/session",
            async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> (
                HttpContext http, IdentityDbContext db, ITokenService tokens, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId() && !u.IsSuspended, ct);
                if (user is null) return TypedResults.Unauthorized();

                var (refresh, refreshValue) = tokens.CreateRefreshToken(user.Id, http.Connection.RemoteIpAddress?.ToString());
                db.RefreshTokens.Add(refresh);
                await db.SaveChangesAsync(ct);

                var access = tokens.CreateAccessToken(user);
                var handedOut = RefreshCookie.Issue(http, user.Id, refreshValue, refresh.ExpiresAtUtc);
                return TypedResults.Ok(new AuthResponse(access.Value, access.ExpiresAtUtc, handedOut, user.ToProfile()));
            })
        .WithName("SessionFromCookie").WithTags("Auth")
        .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute
        {
            AuthenticationSchemes = AuthCookie.SchemeName
        });

        app.MapPost("/api/auth/logout",
            async Task<NoContent> (
                RevokeTokenCommand command, IDispatcher dispatcher, HttpContext context, CancellationToken ct) =>
            {
                await dispatcher.Send(command, ct);
                await AuthCookie.SignOutAsync(context);

                // Always no-content: an unknown refresh token still means "I am signed out".
                return TypedResults.NoContent();
            })
        .WithName("Logout").WithTags("Auth").AllowAnonymous();
    }
}
