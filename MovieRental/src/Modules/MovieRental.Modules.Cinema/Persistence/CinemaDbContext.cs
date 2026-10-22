using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.SharedKernel.Persistence;

namespace MovieRental.Modules.Cinema.Persistence;

public sealed class CinemaDbContext(DbContextOptions<CinemaDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "cinema";
    public override string Schema => SchemaName;

    public DbSet<Screening> Screenings => Set<Screening>();
    public DbSet<SeatBooking> SeatBookings => Set<SeatBooking>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Screening>(e =>
        {
            e.ToTable("Screenings");
            e.HasKey(x => x.Id);
            e.Property(x => x.MovieTitle).HasMaxLength(250).IsRequired();
            e.Property(x => x.Hall).HasMaxLength(60).IsRequired();
            e.Property(x => x.SeatPrice).HasPrecision(10, 2);
            e.Property(x => x.AudioLanguage).HasMaxLength(8).IsRequired();
            e.Property(x => x.SubtitleLanguage).HasMaxLength(8);
            e.HasIndex(x => x.StartsAtUtc);
            // What the Movies on Display filters sort and narrow by.
            e.HasIndex(x => new { x.StartsAtUtc, x.AudioLanguage });
            e.HasMany(x => x.Bookings).WithOne(x => x.Screening!).HasForeignKey(x => x.ScreeningId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SeatBooking>(e =>
        {
            e.ToTable("SeatBookings");
            e.HasKey(x => x.Id);
            e.Property(x => x.PricePaid).HasPrecision(10, 2);

            // Feature 9's real guarantee. Two people clicking the same seat at the same
            // instant both pass the availability read; only one survives this index, and
            // the loser gets a clean "seat just went" instead of a double booking.
            e.HasIndex(x => new { x.ScreeningId, x.Row, x.Number })
             .IsUnique()
             .HasFilter("[IsDeleted] = 0");
        });

        base.OnModelCreating(b);
    }
}
