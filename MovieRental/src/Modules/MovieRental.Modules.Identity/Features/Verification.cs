using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;

namespace MovieRental.Modules.Identity.Features;

// Feature 1 (continued) — six-digit codes over e-mail or SMS.
//
// These endpoints are anonymous on purpose: a freshly registered user has no token yet,
// because the whole point is that they cannot sign in until the code is confirmed. They
// are addressed by e-mail instead, and every failure returns the same shape so the route
// cannot be used to discover which addresses exist.

public sealed record SendVerificationCommand(string Email, VerificationChannel Channel) : ICommand<Result>;

internal sealed class SendVerificationHandler(IdentityDbContext db, IVerificationService verification)
    : ICommandHandler<SendVerificationCommand, Result>
{
    public async Task<Result> Handle(SendVerificationCommand command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Unknown address: report success anyway, send nothing.
        if (user is null) return Result.Success();

        if (command.Channel == VerificationChannel.Email && user.IsEmailConfirmed)
            return Result.Failure(Error.Conflict("This e-mail is already confirmed."));

        return await verification.IssueAsync(user, command.Channel, ct);
    }
}

public sealed record ConfirmCodeCommand(string Email, VerificationChannel Channel, string Code) : ICommand<Result>;

internal sealed class ConfirmCodeHandler(IdentityDbContext db, IVerificationService verification)
    : ICommandHandler<ConfirmCodeCommand, Result>
{
    public async Task<Result> Handle(ConfirmCodeCommand command, CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
            return Result.Failure(Error.Validation("That code has expired or been used up. Ask for a new one."));

        return await verification.ConfirmAsync(user, command.Channel, command.Code, ct);
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
        .WithName("SendVerification").WithTags("Auth").AllowAnonymous();

        app.MapPost("/api/auth/verification/confirm",
            async Task<Results<NoContent, BadRequest<Error>>> (
                ConfirmCodeCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.BadRequest(result.Error);
            })
        .WithName("ConfirmVerificationCode").WithTags("Auth").AllowAnonymous();
    }
}
