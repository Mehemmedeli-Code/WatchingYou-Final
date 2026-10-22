using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Identity.Domain;

/// <summary>Append-only. No soft delete, because an entry that can be removed is not a record.</summary>
public sealed class AuditEntryRow : BaseEntity
{
    public required string Action { get; set; }
    public required string Subject { get; set; }
    public Guid? SubjectId { get; set; }
    public string? Reason { get; set; }

    public Guid ActorId { get; set; }
    public required string ActorName { get; set; }
    public required string ActorRoles { get; set; }

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
