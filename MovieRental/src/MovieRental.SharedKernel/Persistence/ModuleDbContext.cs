using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.SharedKernel.Persistence;

/// <summary>
/// Base context for every module. Each module owns a SQL schema inside the same
/// database, which is what keeps this a modular monolith rather than a distributed
/// system: one connection, one transaction, full ACID guarantees across a slice.
///
/// CAP trade-off: a single MSSQL instance chooses consistency and partition tolerance
/// over availability (CP). During a failover the API returns 503 rather than serving a
/// stale catalogue. That is the right call here — a rental that double-books the last
/// copy is worse than a rental that fails and can be retried.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    public abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
        ApplySoftDeleteFilters(modelBuilder);
    }

    /// <summary>Adds <c>HasQueryFilter(e =&gt; !e.IsDeleted)</c> to every soft-deletable entity.
    /// Admin restore screens opt out with <c>IgnoreQueryFilters()</c>.</summary>
    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is not null) continue;
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(property), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndSoftDelete();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndSoftDelete();
        return base.SaveChanges();
    }

    private void ApplyAuditAndSoftDelete()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAtUtc = now;
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAtUtc = now;
        }

        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>().Where(e => e.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAtUtc = now;
        }
    }
}
