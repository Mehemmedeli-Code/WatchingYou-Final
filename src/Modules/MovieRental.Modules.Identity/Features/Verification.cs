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

// Feature 1 (continued) — six-digit codes by e-mail.
//
// These endpoints are anonymous on purpose: a freshly registered user has no token yet,
// because the whole point is that they cannot sign in until the code is confirmed. They
// are addressed by e-mail instead, and every failure returns the same shape so the route
// cannot be used to discover which addresses exist.

public sealed record SendVerificationCommand(string Email) : ICommand<Result>;

// A body without an e-mail used to reach Email.Trim() and answer 500.
internal sealed class SendVerificationValidator : AbstractValidator<SendVerificationCommand>
{
    public SendVerificationValidator() => RuleFor(x => x.Email).MaximumLength(256);
}

internal sealed class SendVerificationHandler(
    IdentityDbContext db, IVerificationService verification, ICurrentUser currentUser)
    : ICommandHandler<SendVerificationCommand, Result>
{
    public async Task<Result> Handle(SendVerificationCommand command, CancellationToken ct)
    {
        // Signed in: use the account we already know, whatever address was typed.
        var user = currentUser.IsAuthenticated
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.Id, ct)
            : null;

        var signedIn = user is not null;
        if (user is null)
        {
            var email = (command.Email ?? "").Trim().ToLowerInvariant();
            user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        }

        // Unknown address: report success anyway, send nothing.
        if (user is null) return Result.Success();

        // Someone not signed in gets the same answer for every address; only the signed-in
        // owner is told the real reason (already confirmed, wait a minute).
        Result Quiet(Result result) => signedIn ? result : Result.Success();

        if (user.IsEmailConfirmed)
            return Quiet(Result.Failure(Error.Conflict("This e-mail is already confirmed.")));

        return Quiet(await verification.IssueAsync(user, VerificationPurpose.AccountVerification, ct));
    }
}

public sealed record ConfirmCodeCommand(string Email, string Code) : ICommand<Result>;

internal sealed class ConfirmCodeValidator : AbstractValidator<ConfirmCodeCommand>
{
    public ConfirmCodeValidator()
    {
        RuleFor(x => x.Email).MaximumLength(256);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
    }
}

internal sealed class ConfirmCodeHandler(
    IdentityDbContext db, IVerificationService verification, ICurrentUser currentUser)
    : ICommandHandler<ConfirmCodeCommand, Result>
{
    public async Task<Result> Handle(ConfirmCodeCommand command, CancellationToken ct)
    {
        var user = currentUser.IsAuthenticated
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.Id, ct)
            : null;

        if (user is null)
        {
            var email = (command.Email ?? "").Trim().ToLowerInvariant();
            user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        }

        if (user is null)
            return Result.Failure(Error.Validation("That code has expired or been used up. Ask for a new one."));

        var checkResult = await verification.CheckAsync(
            user, VerificationPurpose.AccountVerification, command.Code, ct);
        if (checkResult.IsFailure) return checkResult;

        // Marking the address confirmed is this slice's job, not the code checker's — the
        // same check also guards password resets, which must not confirm anything.
        user.IsEmailConfirmed = true;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class VerificationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/verification/send",
            async Task<Results<NoContent, BadRequest<Error>, Conflict<Error>>> (
                SendVerificationCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "conflict"
                    ? TypedResults.Conflict(result.Error)
                    : TypedResults.BadRequest(result.Error);
            })
        .WithName("SendVerification").WithTags("Auth").AllowAnonymous()
        .WithSummary("Send a six-digit confirmation code by e-mail")
        .WithDescription("Not signed in, the answer is always 204, whether or not the address has an account. Signed in, " +
                         "the real reason is given: 409 when already confirmed or asked again within a minute.")
        .RequireRateLimiting(AppPolicies.CodeRateLimit);

        app.MapPost("/api/auth/verification/confirm",
            async Task<Results<NoContent, BadRequest<Error>>> (
                ConfirmCodeCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.BadRequest(result.Error);
            })
        .WithName("ConfirmVerificationCode").WithTags("Auth").AllowAnonymous()
        .WithSummary("Confirm the e-mail address with the code")
        .WithDescription("400: the code is wrong (`message` says how many tries are left), expired or used up.")
        .RequireRateLimiting(AppPolicies.CodeRateLimit);
    }
}
