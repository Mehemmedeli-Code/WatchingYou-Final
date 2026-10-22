using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Infrastructure;

/// <summary>
/// Lives in the Identity module because the actor is always a user, and this is the module
/// that owns users. Other modules write to it through the contract without knowing that.
/// </summary>
internal sealed class AuditLog(IdentityDbContext db, ICurrentUser currentUser) : IAuditLog
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated) return;

        var actorId = currentUser.RequireId();
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct);

        db.AuditEntries.Add(new AuditEntryRow
        {
            Action = entry.Action,
            Subject = entry.Subject,
            SubjectId = entry.SubjectId,
            Reason = entry.Reason,
            ActorId = actorId,
            // Snapshotted: the name and roles at the time of the act. Looking up today's roles
            // later would quietly rewrite history every time somebody is promoted.
            ActorName = actor?.FullName ?? "Unknown",
            ActorRoles = actor?.Roles ?? "",
            AtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditRecord>> RecentAsync(int take = 100, CancellationToken ct = default) =>
        await db.AuditEntries.AsNoTracking()
            .OrderByDescending(e => e.AtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(e => new AuditRecord(e.Id, e.Action, e.Subject, e.Reason,
                e.ActorId, e.ActorName, e.ActorRoles, e.AtUtc))
            .ToListAsync(ct);
}
