using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Password reset, in two steps: ask for a code, then send it back with a new password.
//
// Both endpoints answer the same way whether or not the address exists. Anything else turns
// the form into a way of discovering who has an account here.

public sealed record RequestPasswordResetCommand(string Email) : ICommand<Result>;

internal sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

internal sealed class RequestPasswordResetHandler(IdentityDbContext db, IVerificationService verification)
    : ICommandHandler<RequestPasswordResetCommand, Result>
{
    public async Task<Result> Handle(RequestPasswordResetCommand command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Unknown address, or one that cannot sign in anyway: report success, send nothing.
        if (user is null || user.IsSuspended) return Result.Success();

        // Always the same answer: a "wait a minute" for some addresses and not others showed
        // which ones have an account.
        await verification.IssueAsync(user, VerificationChannel.Email, VerificationPurpose.PasswordReset, ct);
        return Result.Success();
    }
}

public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword) : ICommand<Result>;

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password needs at least one capital letter.")
            .Matches("[0-9]").WithMessage("Password needs at least one digit.");
    }
}

internal sealed class ResetPasswordHandler(
    IdentityDbContext db, IVerificationService verification, IPasswordHasher hasher)
    : ICommandHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
            return Result.Failure(Error.Validation("That code has expired or been used up. Ask for a new one."));

        var checkResult = await verification.CheckAsync(
            user, VerificationChannel.Email, VerificationPurpose.PasswordReset, command.Code, ct);
        if (checkResult.IsFailure) return checkResult;

        user.PasswordHash = hasher.Hash(command.NewPassword);

        // Proving control of the mailbox also confirms the address, so someone who registered
        // and never clicked through is not left stuck after a reset.
        user.IsEmailConfirmed = true;

        // Every existing session dies. If the reset happened because someone else had the old
        // password, leaving their refresh token alive would defeat the whole exercise.
        var live = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in live)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedReason = "Password reset";
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class ForgotPasswordEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/password/forgot",
            async Task<Results<NoContent, Conflict<Error>>> (
                RequestPasswordResetCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.Conflict(result.Error);
            })
        .WithName("RequestPasswordReset").WithTags("Auth").AllowAnonymous()
        .RequireRateLimiting(AppPolicies.CodeRateLimit);

        app.MapPost("/api/auth/password/reset",
            async Task<Results<NoContent, BadRequest<Error>>> (
                ResetPasswordCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.BadRequest(result.Error);
            })
        .WithName("ResetPassword").WithTags("Auth").AllowAnonymous()
        .RequireRateLimiting(AppPolicies.CodeRateLimit);
    }
}
