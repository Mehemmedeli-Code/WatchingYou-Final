using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Rentals.Domain;
using MovieRental.SharedKernel.Persistence;

namespace MovieRental.Modules.Rentals.Persistence;

public sealed class RentalsDbContext(DbContextOptions<RentalsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "rentals";
    public override string Schema => SchemaName;

    public DbSet<Rental> Rentals => Set<Rental>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Rental>(e =>
        {
            e.ToTable("Rentals");
            e.HasKey(x => x.Id);
            e.Property(x => x.MovieTitle).HasMaxLength(250).IsRequired();
            e.Property(x => x.PosterUrl).HasMaxLength(500);
            e.Property(x => x.DailyPrice).HasPrecision(10, 2);
            e.Property(x => x.BasePrice).HasPrecision(10, 2);
            e.Property(x => x.LateFee).HasPrecision(10, 2);

            e.HasIndex(x => new { x.UserId, x.ReturnedAtUtc });
            e.HasIndex(x => x.DueAtUtc);

            // A customer may not hold two live copies of the same title.
            e.HasIndex(x => new { x.UserId, x.MovieId })
             .IsUnique()
             .HasFilter("[ReturnedAtUtc] IS NULL AND [IsDeleted] = 0");
        });

        b.Entity<Subscription>(e =>
        {
            e.ToTable("Subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Plan).HasMaxLength(40).IsRequired();
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.CardBrand).HasMaxLength(20).IsRequired();
            e.Property(x => x.CardLast4).HasMaxLength(4).IsRequired();
            e.HasIndex(x => new { x.UserId, x.EndsAtUtc });
        });

        base.OnModelCreating(b);
    }
}
