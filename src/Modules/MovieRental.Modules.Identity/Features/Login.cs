using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

public sealed record LoginCommand(string Email, string Password) : ICommand<Result<AuthResponse>>;

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class LoginHandler(
    IdentityDbContext db, IPasswordHasher hasher, ITokenService tokens, IHttpContextAccessor http)
    : ICommandHandler<LoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        // Same message for "no such user" and "wrong password" — anything else is an
        // account-enumeration oracle.
        // The hash is checked even when there is no such user, against a fixed one, so both
        // answers take the same time; a fast "no" used to give away which e-mails exist.
        var passwordOk = hasher.Verify(command.Password, user?.PasswordHash ?? TimingDummy.Value.Value);
        if (user is null || !passwordOk)
            return Result.Failure<AuthResponse>(Error.Unauthorized("E-mail or password is incorrect."));

        // Distinct code so the client can send them to the confirm screen instead of
        // leaving them staring at a rejected password they typed correctly.
        if (user.IsSuspended)
            return Result.Failure<AuthResponse>(new Error("account_suspended",
                user.SuspensionReason is { Length: > 0 } reason
                    ? $"This account is suspended: {reason}"
                    : "This account is suspended. Contact support."));

        if (!user.IsEmailConfirmed)
            return Result.Failure<AuthResponse>(new Error("email_unconfirmed",
                "Confirm your e-mail address before signing in."));

        var (refresh, refreshValue) = tokens.CreateRefreshToken(user.Id, http.HttpContext?.Connection.RemoteIpAddress?.ToString());
        db.RefreshTokens.Add(refresh);
        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await AuthCookie.SignInAsync(http.HttpContext, user);

        var access = tokens.CreateAccessToken(user);
        var handedOut = RefreshCookie.Issue(http.HttpContext, user.Id, refreshValue, refresh.ExpiresAtUtc);
        return Result.Success(new AuthResponse(access.Value, access.ExpiresAtUtc, handedOut, user.ToProfile()));
    }
}

internal static class TimingDummy
{
    public static readonly Lazy<string> Value = new(() => new BCryptPasswordHasher().Hash(Guid.NewGuid().ToString()));
}

public static class LoginEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/login",
            async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult, BadRequest<Error>>> (
                LoginCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);

                // Distinct codes so the client can route: one to the confirm screen, one to
                // a plain explanation. Anything else stays a bare 401 to avoid telling an
                // attacker which half of the pair was wrong.
                return result.Error.Code is "email_unconfirmed" or "account_suspended"
                    ? TypedResults.BadRequest(result.Error)
                    : TypedResults.Unauthorized();
            })
        .WithName("Login")
        .WithTags("Auth")
        .AllowAnonymous()
        .RequireRateLimiting(AppPolicies.AuthRateLimit);
}
