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
    public DbSet<SeatPayment> SeatPayments => Set<SeatPayment>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Hall> Halls => Set<Hall>();

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
            e.Property(x => x.CancellationReason).HasMaxLength(300);
            e.Property(x => x.RefundNote).HasMaxLength(500);
            e.Property(x => x.RefundFeePercent).HasPrecision(5, 2);
            e.HasIndex(x => x.StartsAtUtc);
            // What the Movies on Display filters sort and narrow by.
            e.HasIndex(x => new { x.StartsAtUtc, x.AudioLanguage });
            e.HasIndex(x => x.HallId);
            e.HasOne(x => x.HallRoom).WithMany()
             .HasForeignKey(x => x.HallId).OnDelete(DeleteBehavior.NoAction);
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

        b.Entity<SeatPayment>(e =>
        {
            e.ToTable("SeatPayments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(16).IsRequired();
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.Property(x => x.Last4).HasMaxLength(4).IsRequired();
            e.Property(x => x.CardHolder).HasMaxLength(120).IsRequired();
            e.Property(x => x.RefundedAmount).HasPrecision(10, 2);
            e.Property(x => x.RefundReason).HasMaxLength(300);
            e.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.Salt).HasMaxLength(64).IsRequired();
            e.Property(x => x.Brand).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.ScreeningId, x.Status, x.ExpiresAtUtc });

            // NoAction, not Cascade. SQL Server refuses two cascade paths to the same table,
            // and deleting a Screening already reaches SeatBookings directly — a second route
            // through SeatPayments makes the constraint illegal. Nothing is lost: expired and
            // cancelled holds are removed explicitly in CheckoutHandler, which is clearer than
            // relying on a cascade anyway.
            e.HasMany(x => x.Seats).WithOne(x => x.Payment!)
             .HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Venue>(e =>
        {
            e.ToTable("Venues");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.City).HasMaxLength(80).IsRequired();
            e.Property(x => x.Address).HasMaxLength(250);
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
            e.HasIndex(x => x.Name);
            e.HasMany(x => x.Halls).WithOne(x => x.Venue!)
             .HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Hall>(e =>
        {
            e.ToTable("Halls");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Format).HasMaxLength(80);
            // One "A100" per cinema; the same name in another cinema is a different room.
            e.HasIndex(x => new { x.VenueId, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        base.OnModelCreating(b);
    }
}
