using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// The missing half of SMS verification.
//
// The phone number was only ever collected at registration, and it was optional there. Anyone
// who skipped it could never receive an SMS code, because nothing on the site could give them
// a number to send it to — the button simply never appeared. This is that endpoint.

/// <param name="Username">Null leaves the handle as it is, so older callers that only send a name
/// and phone keep working.</param>
public sealed record UpdateProfileCommand(string FullName, string? PhoneNumber, string? Username = null, bool? IsPrivate = null, string? Bio = null) : ICommand<Result<UserProfileResponse>>;

internal sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.PhoneNumber).Matches(@"^\+?[0-9]{7,15}$")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Use digits only, optionally starting with +.");
        RuleFor(x => x.Username).Must(u => Usernames.IsValid(Usernames.Normalize(u)))
            .When(x => x.Username is not null)
            .WithMessage("Username: 3-30 characters, lower-case letters, digits, dots and underscores.");
        RuleFor(x => x.Username).Must(u => !Usernames.IsReserved(Usernames.Normalize(u)))
            .When(x => x.Username is not null)
            .WithMessage("That username is taken.");
        RuleFor(x => x.Bio).MaximumLength(150);
    }
}

internal sealed class UpdateProfileHandler(IdentityDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateProfileCommand, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(UpdateProfileCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure<UserProfileResponse>(Error.NotFound("User"));

        var phone = string.IsNullOrWhiteSpace(command.PhoneNumber) ? null : command.PhoneNumber.Trim();

        // A new number is an unproven number. Carrying the old confirmation across would mean
        // a "verified" phone that nobody ever verified.
        if (!string.Equals(phone, user.PhoneNumber, StringComparison.Ordinal))
        {
            user.PhoneNumber = phone;
            user.IsPhoneConfirmed = false;
        }

        if (command.Username is not null)
        {
            var handle = Usernames.Normalize(command.Username);
            if (handle != user.Username)
            {
                if (await db.Users.AnyAsync(u => u.Username == handle && u.Id != user.Id, ct))
                    return Result.Failure<UserProfileResponse>(Error.Conflict("That username is taken."));
                user.Username = handle;
            }
        }

        if (command.IsPrivate is { } isPrivate && isPrivate != user.IsPrivate)
        {
            user.IsPrivate = isPrivate;
            // Going public answers every waiting request with yes: there is nothing left to approve.
            if (!isPrivate)
                await db.Follows.Where(f => f.FolloweeId == user.Id && !f.IsAccepted)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsAccepted, true), ct);
        }

        // Null leaves the bio alone; an empty string clears it.
        if (command.Bio is not null) user.Bio = string.IsNullOrWhiteSpace(command.Bio) ? null : command.Bio.Trim();

        user.FullName = command.FullName.Trim();
        await db.SaveChangesAsync(ct);

        return Result.Success(user.ToProfile());
    }
}

public static class UpdateProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/api/auth/profile",
            async Task<Results<Ok<UserProfileResponse>, BadRequest<Error>, NotFound<Error>, Conflict<Error>>> (
                UpdateProfileCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            })
        .WithName("UpdateProfile").WithTags("Auth").RequireAuthorization();
}
