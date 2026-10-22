namespace MovieRental.SharedKernel.Abstractions;

/// <summary>Marks an entity that is never physically removed. The DbContext turns a
/// Remove() into an UPDATE and a global query filter hides the row afterwards.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
}
