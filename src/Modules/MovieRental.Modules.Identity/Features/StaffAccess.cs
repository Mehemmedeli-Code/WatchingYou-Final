using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// The staff PIN: a second question on the way into any staff screen. A session taken from a
// staff member's browser, or a password guessed, is not enough on its own to open the panel.

public static class StaffPin
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    /// <summary>4 to 12 digits. Short enough to type at a till, long enough to choose well.</summary>
    public static bool IsWellFormed(string? pin) =>
        pin is { Length: >= 4 and <= 12 } && pin.All(char.IsAsciiDigit);
}

public enum StaffUnlockOutcome { Unlocked, Wrong, Locked, NoPin, NotStaff }

public sealed record StaffUnlockResult(StaffUnlockOutcome Outcome, DateTime? LockedUntilUtc = null, int AttemptsLeft = 0);

public sealed record UnlockStaffCommand(string Pin) : ICommand<StaffUnlockResult>;

internal sealed class UnlockStaffHandler(IdentityDbContext db, ICurrentUser currentUser, IPasswordHasher hasher, IAuditLog audit)
    : ICommandHandler<UnlockStaffCommand, StaffUnlockResult>
{
    public async Task<StaffUnlockResult> Handle(UnlockStaffCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null || user.IsSuspended || !user.RoleList.Intersect(AppRoles.Staff).Any())
            return new(StaffUnlockOutcome.NotStaff);
        if (string.IsNullOrEmpty(user.StaffPinHash)) return new(StaffUnlockOutcome.NoPin);

        var now = DateTime.UtcNow;
        if (user.StaffPinLockedUntilUtc > now) return new(StaffUnlockOutcome.Locked, user.StaffPinLockedUntilUtc);

        // The hash is checked even for a malformed PIN, so a wrong guess costs the same time.
        var right = hasher.Verify(command.Pin ?? "", user.StaffPinHash) && StaffPin.IsWellFormed(command.Pin);
        if (right)
        {
            user.StaffPinFailures = 0;
            user.StaffPinLockedUntilUtc = null;
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEntry("staff.unlocked", user.Email, null, user.Id), ct);
            return new(StaffUnlockOutcome.Unlocked);
        }

        user.StaffPinFailures++;
        var locked = user.StaffPinFailures >= StaffPin.MaxFailures;
        if (locked)
        {
            user.StaffPinFailures = 0;
            user.StaffPinLockedUntilUtc = now + StaffPin.Lockout;
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(locked ? "staff.pin-locked" : "staff.pin-wrong", user.Email, null, user.Id), ct);
        return locked
            ? new(StaffUnlockOutcome.Locked, user.StaffPinLockedUntilUtc)
            : new(StaffUnlockOutcome.Wrong, AttemptsLeft: StaffPin.MaxFailures - user.StaffPinFailures);
    }
}

/// <summary>The owner sets each staff member's PIN. Null clears it, which closes the staff
/// screens to that person until a new one is set.</summary>
public sealed record SetStaffPinCommand([property: JsonIgnore] Guid UserId, string? Pin) : ICommand<Result>;

internal sealed class SetStaffPinValidator : AbstractValidator<SetStaffPinCommand>
{
    public SetStaffPinValidator() =>
        RuleFor(c => c.Pin).Must(pin => pin is null || StaffPin.IsWellFormed(pin)).WithMessage("The PIN is 4 to 12 digits.");
}

internal sealed class SetStaffPinHandler(IdentityDbContext db, IPasswordHasher hasher, IAuditLog audit)
    : ICommandHandler<SetStaffPinCommand, Result>
{
    public async Task<Result> Handle(SetStaffPinCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));
        if (!user.RoleList.Intersect(AppRoles.Staff).Any())
            return Result.Failure(Error.Conflict("Only staff have a PIN. Give this person a staff role first."));
        // The owner's own PIN lives in the server's configuration, out of reach of the site.
        if (user.RoleList.Contains(AppRoles.SuperAdmin))
            return Result.Failure(Error.Conflict("The owner's PIN is set on the server (Staff:OwnerPin), not here."));

        user.StaffPinHash = command.Pin is null ? null : hasher.Hash(command.Pin);
        user.StaffPinFailures = 0;
        user.StaffPinLockedUntilUtc = null;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(command.Pin is null ? "staff.pin-cleared" : "staff.pin-set", user.Email, null, user.Id), ct);
        return Result.Success();
    }
}

/// <summary>
/// Run at start-up. The account named in Staff:OwnerEmail is the one SuperAdmin; anyone else
/// holding the role (an edited database, an old owner) loses it. Staff:OwnerPin, when set,
/// becomes the owner's PIN.
/// </summary>
public static class StaffOwner
{
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StaffOwner));

        var email = config["Staff:OwnerEmail"]?.Trim().ToLowerInvariant();
        var pin = config["Staff:OwnerPin"];

        foreach (var other in await db.Users.Where(u => u.Roles.Contains(AppRoles.SuperAdmin)).ToListAsync(ct))
            if (other.Email != email)
                other.Roles = string.Join(',', other.RoleList.Where(r => r != AppRoles.SuperAdmin));

        var owner = string.IsNullOrEmpty(email) ? null : await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (owner is null)
            log.LogWarning("Staff:OwnerEmail has no account yet; nobody is SuperAdmin until it registers and the site restarts.");
        else
        {
            owner.Roles = string.Join(',', owner.RoleList.Union([AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Customer]));
            owner.IsSuspended = false;
            if (StaffPin.IsWellFormed(pin) && (owner.StaffPinHash is null || !hasher.Verify(pin!, owner.StaffPinHash)))
            {
                owner.StaffPinHash = hasher.Hash(pin!);
                owner.StaffPinLockedUntilUtc = null;
                owner.StaffPinFailures = 0;
            }
            else if (!string.IsNullOrEmpty(pin) && !StaffPin.IsWellFormed(pin))
                log.LogWarning("Staff:OwnerPin is not 4 to 12 digits and was ignored.");
            if (owner.StaffPinHash is null)
                log.LogWarning("The owner has no staff PIN yet. Set Staff:OwnerPin in user secrets.");
        }
        await db.SaveChangesAsync(ct);
    }
}

public static class StaffPinEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/api/admin/users/{id:guid}/staff-pin",
                async Task<Results<NoContent, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                    Guid id, SetStaffPinCommand body, IDispatcher dispatcher, CancellationToken ct) =>
                {
                    var result = await dispatcher.Send(body with { UserId = id }, ct);
                    if (result.IsSuccess) return TypedResults.NoContent();
                    return result.Error.Code switch
                    {
                        "not_found" => TypedResults.NotFound(result.Error),
                        "conflict" => TypedResults.Conflict(result.Error),
                        _ => TypedResults.BadRequest(result.Error)
                    };
                })
            .WithName("SetStaffPin").WithTags("Users").RequireAuthorization(AppRoles.SuperAdmin)
            .WithSummary("Set or clear a staff member's PIN")
            .WithDescription("`pin`: 4 to 12 digits, or null to clear it (that person cannot open staff screens until a new one is set). " +
                             "409: not a member of staff, or the owner, whose PIN is set on the server.");
}
