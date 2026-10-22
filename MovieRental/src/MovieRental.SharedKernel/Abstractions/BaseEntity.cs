namespace MovieRental.SharedKernel.Abstractions;

/// <summary>Root of every persisted aggregate. Guid keys keep modules independent of
/// a shared identity sequence, which matters once modules own separate schemas.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
