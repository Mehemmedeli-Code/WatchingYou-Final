using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
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
public sealed record RegisterCommand(string FullName, string Email, string Password, string? Gender = null)
    : ICommand<Result<RegistrationResponse>>;

internal sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Gender).Must(Genders.IsValid).WithMessage("Choose whether you are a man or a woman.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password needs at least one capital letter.")
            .Matches("[0-9]").WithMessage("Password needs at least one digit.");
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

        // A handle from the e-mail, with a short suffix only when that one is already taken.
        var handle = Usernames.FromEmail(normalizedEmail);
        if (await db.Users.AnyAsync(u => u.Username == handle, ct))
            handle = $"{handle}_{Random.Shared.Next(1000, 9999)}";

        var user = new AppUser
        {
            Email = normalizedEmail,
            FullName = command.FullName.Trim(),
            Username = handle,
            Gender = command.Gender,
            PasswordHash = hasher.Hash(command.Password),
            Roles = AppRoles.Customer
        };

        db.Users.Add(user);
        for (var attempt = 1; ; attempt++)
        {
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException)
            {
                // Sent twice at once: both passed the check above and the unique index kept one.
                if (await db.Users.AsNoTracking().AnyAsync(u => u.Email == normalizedEmail, ct))
                    return Result.Failure<RegistrationResponse>(Error.Conflict("That e-mail is already registered."));
                // Otherwise the handle: ali@gmail.com and ali@mail.ru signing up at the same moment
                // both get "ali", or the suffix drew one already in use. Draw another.
                if (attempt == 3 || !await db.Users.AsNoTracking().AnyAsync(u => u.Username == user.Username, ct)) throw;
                user.Username = $"{Usernames.FromEmail(normalizedEmail)}_{Random.Shared.Next(1000, 9999)}";
            }
        }

        // The account is created either way. If the mail transport is down the user can ask
        // for another code from the confirm screen rather than registering all over again.
        try
        {
            var issued = await verification.IssueAsync(
                user, VerificationPurpose.AccountVerification, ct);
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
        .WithSummary("Create an account; a six-digit code is e-mailed to confirm it")
        .WithDescription("Sign-in works once the code is confirmed (POST /api/auth/verification/confirm). " +
                         "Password: 8+ characters with a capital letter and a digit. gender: \"female\" or \"male\". 409: the e-mail is already registered.")
        .AllowAnonymous()
        .RequireRateLimiting(AppPolicies.AuthRateLimit);
}

internal static class UserProfileMapper
{
    public static UserProfileResponse ToProfile(this AppUser user) => new(
        user.Id, user.FullName, user.Email,
        user.IsEmailConfirmed, user.RoleList,
        user.ShareOnGlobe, user.City, user.AvatarUrl, user.Username, user.IsPrivate, user.Bio, user.Gender);
}

public static class Genders
{
    public static bool IsValid(string? gender) => gender is "female" or "male";
}
