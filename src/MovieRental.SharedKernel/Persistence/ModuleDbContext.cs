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

    /// <summary>
    /// Every DateTime in this solution is UTC — the properties say so in their names — but SQL
    /// Server's datetime2 does not store a kind, so values came back Unspecified and were
    /// serialised without a trailing "Z". Browsers then read them as local time, and a show at
    /// 15:00 UTC appeared at 15:00 in Baku instead of 19:00. Marking them UTC on the way out
    /// of the database fixes every endpoint at once.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        base.ConfigureConventions(configurationBuilder);
    }

    private sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

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
