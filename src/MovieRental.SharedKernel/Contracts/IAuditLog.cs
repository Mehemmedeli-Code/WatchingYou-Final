namespace MovieRental.SharedKernel.Contracts;

/// <summary>
/// Who did what, and why.
///
/// The project separates inspection from publication and gates roles behind an admin, but
/// separation of duties without a record is only half the idea: if nobody can see who
/// approved a flagged film or who suspended an account, the split protects nothing.
///
/// Deliberately append-only. There is no update and no delete, because a log that can be
/// edited by the people it watches is not evidence.
/// </summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditRecord>> RecentAsync(int take = 100, CancellationToken ct = default);
}

/// <param name="Action">Dotted and stable — "shortfilm.approved", "user.suspended".</param>
/// <param name="Subject">What it happened to, in words a human reads: a title, an e-mail.</param>
/// <param name="Reason">Why. The interesting column, and the one worth filling in.</param>
public sealed record AuditEntry(string Action, string Subject, string? Reason = null, Guid? SubjectId = null);

public sealed record AuditRecord(
    Guid Id, string Action, string Subject, string? Reason,
    Guid ActorId, string ActorName, string ActorRoles, DateTime AtUtc);
