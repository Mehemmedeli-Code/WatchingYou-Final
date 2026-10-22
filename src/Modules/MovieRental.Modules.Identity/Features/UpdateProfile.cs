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

// The profile: name, @username, privacy and bio.

/// <param name="Username">Null leaves the handle as it is, so older callers that only send a
/// name keep working.</param>
public sealed record UpdateProfileCommand(string FullName, string? Username = null, bool? IsPrivate = null, string? Bio = null, string? Gender = null) : ICommand<Result<UserProfileResponse>>;

internal sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Username).Must(u => Usernames.IsValid(Usernames.Normalize(u)))
            .When(x => x.Username is not null)
            .WithMessage("Username: 3-30 characters, lower-case letters, digits, dots and underscores.");
        RuleFor(x => x.Username).Must(u => !Usernames.IsReserved(Usernames.Normalize(u)))
            .When(x => x.Username is not null)
            .WithMessage("That username is taken.");
        RuleFor(x => x.Bio).MaximumLength(150);
        RuleFor(x => x.Gender).Must(Genders.IsValid).When(x => x.Gender is not null)
            .WithMessage("Choose whether you are a man or a woman.");
    }
}

internal sealed class UpdateProfileHandler(IdentityDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateProfileCommand, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(UpdateProfileCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure<UserProfileResponse>(Error.NotFound("User"));

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

        // Chosen once: an account that registered before it was asked picks it here, and nobody
        // switches it later to slip into the other side's searches.
        if (command.Gender is not null && command.Gender != user.Gender)
        {
            if (user.Gender is not null) return Result.Failure<UserProfileResponse>(Error.Validation("Gender cannot be changed once chosen."));
            user.Gender = command.Gender;
        }

        // Null leaves the bio alone; an empty string clears it.
        if (command.Bio is not null) user.Bio = string.IsNullOrWhiteSpace(command.Bio) ? null : command.Bio.Trim();

        user.FullName = command.FullName.Trim();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two people saved the same new handle at once: both passed the check above and the
            // unique index kept the first.
            var handle = user.Username;
            if (handle is null || !await db.Users.AsNoTracking().AnyAsync(u => u.Username == handle && u.Id != user.Id, ct)) throw;
            return Result.Failure<UserProfileResponse>(Error.Conflict("That username is taken."));
        }

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
        .WithName("UpdateProfile").WithTags("Auth").RequireAuthorization()
        .WithSummary("Change my name, username, privacy or bio")
        .WithDescription("Fields left out (null) stay as they are; `fullName` is always needed. `gender` (\"female\"/\"male\") is set once. 409: the username is taken.");
}
