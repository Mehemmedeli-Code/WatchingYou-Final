using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Feature 1 — registration. One file holds the whole slice: contract, rules, handler, route.
public sealed record RegisterCommand(string FullName, string Email, string Password, string? PhoneNumber)
    : ICommand<Result<RegistrationResponse>>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password needs at least one capital letter.")
            .Matches("[0-9]").WithMessage("Password needs at least one digit.");
        RuleFor(x => x.PhoneNumber).Matches(@"^\+?[0-9]{7,15}$")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Use digits only, optionally starting with +.");
    }
}

internal sealed class RegisterHandler(
    IdentityDbContext db,
    IPasswordHasher hasher,
    IVerificationService verification,
    ILogger<RegisterHandler> logger)
    : ICommandHandler<RegisterCommand, Result<RegistrationResponse>>
{
    public async Task<Result<RegistrationResponse>> Handle(RegisterCommand command, CancellationToken ct)
    {
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct))
            return Result.Failure<RegistrationResponse>(Error.Conflict("That e-mail is already registered."));

        var user = new AppUser
        {
            Email = normalizedEmail,
            FullName = command.FullName.Trim(),
            PhoneNumber = command.PhoneNumber?.Trim(),
            PasswordHash = hasher.Hash(command.Password),
            Roles = AppRoles.Customer
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        // The account is created either way. If the mail transport is down the user can ask
        // for another code from the confirm screen rather than registering all over again.
        try
        {
            var issued = await verification.IssueAsync(user, VerificationChannel.Email, ct);
            if (issued.IsFailure) throw new InvalidOperationException(issued.Error.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send the verification code to {Email}", user.Email);
            return Result.Success(new RegistrationResponse(user.Email, false,
                "Your account was created, but the code could not be sent. Try requesting it again."));
        }

        return Result.Success(new RegistrationResponse(user.Email, true,
            "We sent a six-digit code to your e-mail. Enter it to finish signing up."));
    }
}

public static class RegisterEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/register",
            async Task<Results<Ok<RegistrationResponse>, Conflict<Error>>> (
                RegisterCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                return result.IsSuccess
                    ? TypedResults.Ok(result.Value)
                    : TypedResults.Conflict(result.Error);
            })
        .WithName("Register")
        .WithTags("Auth")
        .AllowAnonymous();
}

internal static class UserProfileMapper
{
    public static UserProfileResponse ToProfile(this AppUser user) => new(
        user.Id, user.FullName, user.Email, user.PhoneNumber,
        user.IsEmailConfirmed, user.IsPhoneConfirmed, user.RoleList);
}
