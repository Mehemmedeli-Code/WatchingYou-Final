using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Deleting your own account.
//
// Both app stores require it of any app that lets people sign up (Apple's guideline
// 5.1.1(v); Google Play's account-deletion policy), and it is right on its own terms: an
// account you cannot close is not really yours.
//
// What "deleted" means here: the person is gone, the records are not. Their name, e-mail,
// phone, city and picture are wiped and every session is ended, so nothing identifies them
// any more. Bookings and payments stay, attached to an anonymous row — the cinema still has
// to account for money it took — but they no longer lead back to anyone.

public sealed record DeleteAccountCommand(string Password) : ICommand<Result>;

internal sealed class DeleteAccountHandler(
    IdentityDbContext db, ICurrentUser currentUser, IPasswordHasher hasher, IAuditLog audit, IHttpContextAccessor http)
    : ICommandHandler<DeleteAccountCommand, Result>
{
    public async Task<Result> Handle(DeleteAccountCommand command, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure(Error.NotFound("Account"));

        // The password again, so a phone left unlocked on a table cannot be used to do this.
        if (string.IsNullOrEmpty(command.Password) || !hasher.Verify(command.Password, user.PasswordHash))
            return Result.Failure(Error.Validation("That password is not correct."));

        // Staff accounts are closed by another admin from the admin page, never by themselves:
        // an admin deleting their own account could leave the cinema with nobody in charge.
        if (user.RoleList.Contains(AppRoles.Admin))
            return Result.Failure(Error.Forbidden("An administrator account cannot be deleted from here."));

        var email = user.Email;
        var now = DateTime.UtcNow;

        // Anonymise first, then soft-delete: the row survives for the bookings that point at
        // it, but says nothing about who it was.
        user.Email = $"deleted-{user.Id:N}@deleted.invalid";
        user.FullName = "Deleted user";
        user.PhoneNumber = null;
        user.IsPhoneConfirmed = false;
        user.PasswordHash = hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        user.ShareOnGlobe = false;
        user.City = null;
        user.CountryCode = null;
        user.Latitude = null;
        user.Longitude = null;
        user.AvatarUrl = null;

        foreach (var token in user.RefreshTokens.Where(t => t.RevokedAtUtc == null))
        {
            token.RevokedAtUtc = now;
            token.RevokedReason = "account deleted";
        }

        db.Users.Remove(user);   // soft delete: the query filter hides the row from here on
        await db.SaveChangesAsync(ct);
        await AuthCookie.SignOutAsync(http.HttpContext);

        // The log names the account by id only — keeping the e-mail here would undo the point.
        await audit.RecordAsync(new AuditEntry("user.self-deleted", $"Account {user.Id:N}",
            $"Closed by its owner ({email.Split('@')[^1]} address)", user.Id), ct);

        return Result.Success();
    }
}

public static class DeleteAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/account/delete",
                async Task<Results<NoContent, BadRequest<Error>, ForbidHttpResult, NotFound<Error>>> (
                    DeleteAccountCommand body, IDispatcher dispatcher, CancellationToken ct) =>
                {
                    var result = await dispatcher.Send(body, ct);
                    if (result.IsSuccess) return TypedResults.NoContent();
                    return result.Error.Code switch
                    {
                        "not_found" => TypedResults.NotFound(result.Error),
                        "forbidden" => TypedResults.Forbid(),
                        _ => TypedResults.BadRequest(result.Error)
                    };
                })
            .WithName("DeleteOwnAccount")
            .WithTags("Auth")
            .RequireAuthorization()
            .RequireRateLimiting(AppPolicies.AuthRateLimit);
}
