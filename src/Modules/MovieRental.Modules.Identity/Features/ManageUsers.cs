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
    Guid Id, string FullName, string Email, string? PhoneNumber,
    string[] Roles, bool IsEmailConfirmed, bool IsPhoneConfirmed,
    bool IsSuspended, string? SuspensionReason, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc);

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
                                  || u.Roles.Contains(AppRoles.Cashier));
        }

        var rows = await users.OrderBy(u => u.Email).Take(200).ToListAsync(ct);

        return [.. rows.Select(u => new AdminUserItem(
            u.Id, u.FullName, u.Email, u.PhoneNumber, u.RoleList,
            u.IsEmailConfirmed, u.IsPhoneConfirmed, u.IsSuspended, u.SuspensionReason,
            u.CreatedAtUtc, u.LastLoginAtUtc))];
    }
}

public sealed record SetUserRolesCommand(Guid UserId, string[] Roles) : ICommand<Result>;

internal sealed class SetUserRolesHandler(IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<SetUserRolesCommand, Result>
{
    private static readonly string[] Known = [AppRoles.Admin, AppRoles.Security, AppRoles.Cashier, AppRoles.Customer];

    public async Task<Result> Handle(SetUserRolesCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        var unknown = command.Roles.Where(role => !Known.Contains(role, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (unknown.Length > 0)
            return Result.Failure(Error.Validation($"Unknown role(s): {string.Join(", ", unknown)}."));

        // Everyone keeps Customer: it is what grants renting and the Studio, and stripping it
        // would leave an admin unable to use the site they administer.
        var roles = command.Roles
            .Select(role => Known.First(k => k.Equals(role, StringComparison.OrdinalIgnoreCase)))
            .Append(AppRoles.Customer)
            .Distinct()
            .ToArray();

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

public sealed record SetUserSuspensionCommand(Guid UserId, bool Suspended, string? Reason) : ICommand<Result>;

internal sealed class SetUserSuspensionHandler(IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<SetUserSuspensionCommand, Result>
{
    public async Task<Result> Handle(SetUserSuspensionCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null) return Result.Failure(Error.NotFound("User"));

        if (user.Id == currentUser.RequireId() && command.Suspended)
            return Result.Failure(Error.Conflict("You cannot suspend yourself."));

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
                Results.Ok(await dispatcher.Ask(new GetUsersQuery(search), ct)))
            .WithName("GetUsers");

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
            }).WithName("SetUserRolesWithId");

        admin.MapPut("/{id:guid}/suspension",
            async Task<Results<NoContent, Conflict<Error>, NotFound<Error>>> (
                Guid id, SetUserSuspensionCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { UserId = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Conflict(result.Error);
            }).WithName("SetUserSuspensionWithId");
    }
}

/// <summary>The log, read-only, for admins. There is no endpoint that edits or deletes an
/// entry — a record the watched can rewrite is not a record.</summary>
public static class AuditEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/audit", async (int? take, IAuditLog audit, CancellationToken ct) =>
                Results.Ok(await audit.RecentAsync(take ?? 100, ct)))
            .WithName("GetAuditLog").WithTags("Users").RequireAuthorization(AppRoles.Admin);
}
