using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Roles existed but could only be handed out by the seeder, so a second Security reviewer
// was impossible without editing code. This is the missing half of the role system.

public sealed record AdminUserItem(
    Guid Id, string FullName, string Email,
    string[] Roles, bool IsEmailConfirmed,
    bool IsSuspended, string? SuspensionReason, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc,
    bool HasStaffPin);

public sealed record GetUsersQuery(string? Search) : IQuery<IReadOnlyList<AdminUserItem>>;

internal sealed class GetUsersHandler(IdentityDbContext db)
    : IQueryHandler<GetUsersQuery, IReadOnlyList<AdminUserItem>>
{
    public async Task<IReadOnlyList<AdminUserItem>> Handle(GetUsersQuery query, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            if (term.Length > 100) term = term[..100];
            users = users.Where(u => u.Email.Contains(term) || u.FullName.Contains(term));
        }
        else
        {
            // With nothing typed the list is the staff only: customers are found by searching,
            // when one is to be given a role.
            users = users.Where(u => u.Roles.Contains(AppRoles.Admin) || u.Roles.Contains(AppRoles.Security)
                                  || u.Roles.Contains(AppRoles.Cashier) || u.Roles.Contains(AppRoles.Moderator));
        }

        var rows = await users.OrderBy(u => u.Email).Take(200).ToListAsync(ct);

        return [.. rows.Select(u => new AdminUserItem(
            u.Id, u.FullName, u.Email, u.RoleList,
            u.IsEmailConfirmed, u.IsSuspended, u.SuspensionReason,
            u.CreatedAtUtc, u.LastLoginAtUtc, u.StaffPinHash != null))];
    }
}

/// <param name="UserId">From the address; not read from the body.</param>
public sealed record SetUserRolesCommand([property: JsonIgnore] Guid UserId, string[] Roles) : ICommand<Result>;

// "roles" left out used to reach Roles.Where and answer 500.
internal sealed class SetUserRolesValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesValidator()
    {
        RuleFor(x => x.Roles).NotNull().WithMessage("List the roles to give, for example Security. Customer is always kept.");
        RuleForEach(x => x.Roles).NotEmpty().WithMessage("A role name is empty.");
    }
}

internal sealed class SetUserRolesHandler(IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<SetUserRolesCommand, Result>
{
    // SuperAdmin is not here on purpose: it is never handed out through the site.
    private static readonly string[] Known = [AppRoles.Admin, AppRoles.Security, AppRoles.Cashier, AppRoles.Moderator, AppRoles.Customer];

    public async Task<Result> Handle(SetUserRolesCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var unknown = command.Roles.Where(role => !Known.Contains(role, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (unknown.Length > 0)
            return Result.Failure(Error.Validation($"Unknown role(s): {string.Join(", ", unknown)}."));

        if (user.RoleList.Contains(AppRoles.SuperAdmin))
            return Result.Failure(Error.Conflict("The owner's roles cannot be changed."));

        // Everyone keeps Customer: it is what grants renting and the Studio, and stripping it
        // would leave an admin unable to use the site they administer.
        var roles = command.Roles
            .Select(role => Known.First(k => k.Equals(role, StringComparison.OrdinalIgnoreCase)))
            .Append(AppRoles.Customer)
            .Distinct()
            .ToArray();

        // Only the owner makes or unmakes admins: one admin cannot quietly promote a friend or
        // push the others out.
        if (user.RoleList.Contains(AppRoles.Admin) != roles.Contains(AppRoles.Admin) && !currentUser.IsInRole(AppRoles.SuperAdmin))
            return Result.Failure(Error.Conflict("Only the owner can give or take the Admin role."));

        // An admin removing their own Admin role locks the last door behind them, and there
        // is no way back in through the interface.
        if (user.Id == currentUser.RequireId() && !roles.Contains(AppRoles.Admin))
            return Result.Failure(Error.Conflict("You cannot remove your own Admin role."));

        if (user.RoleList.Contains(AppRoles.Admin) && !roles.Contains(AppRoles.Admin))
        {
            var remainingAdmins = await db.Users.CountAsync(
                u => u.Id != user.Id && u.Roles.Contains(AppRoles.Admin) && !u.IsSuspended, ct);

            if (remainingAdmins == 0)
                return Result.Failure(Error.Conflict("This is the last admin. Promote someone else first."));
        }

        var before = user.Roles;
        user.Roles = string.Join(',', roles);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(
            "user.roles", user.Email, $"{before} → {user.Roles}", user.Id), ct);

        return Result.Success();
    }
}

/// <param name="UserId">From the address; not read from the body.</param>
public sealed record SetUserSuspensionCommand([property: JsonIgnore] Guid UserId, bool Suspended, string? Reason = null) : ICommand<Result>;

internal sealed class SetUserSuspensionValidator : AbstractValidator<SetUserSuspensionCommand>
{
    public SetUserSuspensionValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

internal sealed class SetUserSuspensionHandler(IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<SetUserSuspensionCommand, Result>
{
    public async Task<Result> Handle(SetUserSuspensionCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        if (user.Id == currentUser.RequireId() && command.Suspended)
            return Result.Failure(Error.Conflict("You cannot suspend yourself."));

        if (user.RoleList.Contains(AppRoles.SuperAdmin))
            return Result.Failure(Error.Conflict("The owner cannot be suspended."));

        if (user.RoleList.Contains(AppRoles.Admin) && !currentUser.IsInRole(AppRoles.SuperAdmin))
            return Result.Failure(Error.Conflict("Only the owner can suspend an admin."));

        if (command.Suspended && user.RoleList.Contains(AppRoles.Admin))
        {
            var remainingAdmins = await db.Users.CountAsync(
                u => u.Id != user.Id && u.Roles.Contains(AppRoles.Admin) && !u.IsSuspended, ct);

            if (remainingAdmins == 0)
                return Result.Failure(Error.Conflict("This is the last active admin."));
        }

        user.IsSuspended = command.Suspended;
        user.SuspendedAtUtc = command.Suspended ? DateTime.UtcNow : null;
        user.SuspensionReason = command.Suspended ? command.Reason?.Trim() : null;

        if (command.Suspended)
        {
            // Blocking sign-in is not enough on its own: a live refresh token would keep the
            // suspended account working until it happened to expire.
            var live = await db.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var token in live)
            {
                token.RevokedAtUtc = DateTime.UtcNow;
                token.RevokedReason = "Account suspended";
            }
        }

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(
            command.Suspended ? "user.suspended" : "user.reinstated",
            user.Email, command.Reason, user.Id), ct);

        return Result.Success();
    }
}

public static class ManageUsersEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/users").WithTags("Users").RequireAuthorization(AppRoles.Admin);

        admin.MapGet("", async (string? search, IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.Ask(new GetUsersQuery(search), ct)))
            .WithName("GetUsers")
            .WithSummary("List accounts: the staff, or everyone matching a search")
            .WithDescription("`search` matches the e-mail or the name (up to 200 rows). Left empty, the list is the staff.");

        admin.MapPut("/{id:guid}/roles",
            async Task<Results<NoContent, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, SetUserRolesCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { UserId = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            }).WithName("SetUserRolesWithId")
            .WithSummary("Set an account's roles")
            .WithDescription("Roles: Admin, Security, Cashier, Moderator, Customer (Customer is always kept). Only the owner gives " +
                             "or takes Admin. 409: the owner's roles, your own Admin role, or the last admin.");

        admin.MapPut("/{id:guid}/suspension",
            async Task<Results<NoContent, Conflict<Error>, NotFound<Error>>> (
                Guid id, SetUserSuspensionCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { UserId = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Conflict(result.Error);
            }).WithName("SetUserSuspensionWithId")
            .WithSummary("Suspend an account, or lift the suspension")
            .WithDescription("Suspending ends every session of the account; `reason` is shown to them at sign-in. " +
                             "409: yourself, the owner, an admin (unless you are the owner), or the last active admin.");
    }
}

/// <summary>The log, read-only, for admins. There is no endpoint that edits or deletes an
/// entry — a record the watched can rewrite is not a record.</summary>
public static class AuditEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/audit", async (int? take, IAuditLog audit, CancellationToken ct) =>
                TypedResults.Ok(await audit.RecentAsync(take ?? 100, ct)))
            .WithName("GetAuditLog").WithTags("Users").RequireAuthorization(AppRoles.Admin)
            .WithSummary("The audit log: who did what, newest first")
            .WithDescription("`take`: how many entries, 1 to 500 (default 100). Read-only: no call edits or deletes an entry.");
}
