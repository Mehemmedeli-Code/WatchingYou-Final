using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;

namespace MovieRental.Modules.Identity.Features;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<Result<AuthResponse>>;

internal sealed class RefreshTokenHandler(
    IdentityDbContext db, ITokenService tokens, IHttpContextAccessor http)
    : ICommandHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == command.RefreshToken, ct);

        if (stored?.User is null)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Unknown refresh token."));

        // Replay detection: a token that was already rotated should never come back.
        // When it does, the chain is compromised, so every live token for the user dies.
        if (stored.IsRevoked)
        {
            await RevokeDescendantsAsync(stored, ct);
            await db.SaveChangesAsync(ct);
            return Result.Failure<AuthResponse>(Error.Unauthorized("This session was ended. Sign in again."));
        }

        if (!stored.IsActive)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Refresh token has expired."));

        var ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var replacement = tokens.CreateRefreshToken(stored.UserId, ip);

        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.RevokedReason = "rotated";
        stored.ReplacedByToken = replacement.Token;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(stored.User);
        return Result.Success(new AuthResponse(access.Value, access.ExpiresAtUtc, replacement.Token, stored.User.ToProfile()));
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

public sealed record RevokeTokenCommand(string RefreshToken) : ICommand<Result>;

internal sealed class RevokeTokenHandler(IdentityDbContext db) : ICommandHandler<RevokeTokenCommand, Result>
{
    public async Task<Result> Handle(RevokeTokenCommand command, CancellationToken ct)
    {
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == command.RefreshToken, ct);
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
