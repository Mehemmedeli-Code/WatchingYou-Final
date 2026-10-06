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
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<LoyaltyEntry> LoyaltyEntries => Set<LoyaltyEntry>();

    // Back office
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<ConcessionItem> ConcessionItems => Set<ConcessionItem>();
    public DbSet<ConcessionSale> ConcessionSales => Set<ConcessionSale>();
    public DbSet<ConcessionSaleLine> ConcessionSaleLines => Set<ConcessionSaleLine>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<FilmDeal> FilmDeals => Set<FilmDeal>();

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
            e.Property(x => x.TicketType).HasMaxLength(40);

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
            e.Property(x => x.Provider).HasConversion<int>();
            e.Property(x => x.Tender).HasConversion<int>();
            e.HasIndex(x => x.ShiftId);
            e.Property(x => x.Subtotal).HasPrecision(10, 2);
            e.Property(x => x.PromoCode).HasMaxLength(32);
            e.Property(x => x.PromoDiscount).HasPrecision(10, 2);
            e.Property(x => x.PointsDiscount).HasPrecision(10, 2);
            e.Property(x => x.ExternalSessionId).HasMaxLength(200);
            e.Property(x => x.ExternalPaymentId).HasMaxLength(200);
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

        b.Entity<PromoCode>(e =>
        {
            e.ToTable("PromoCodes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.PercentOff).HasPrecision(5, 2);
            e.Property(x => x.AmountOff).HasPrecision(10, 2);
            e.Property(x => x.MinSubtotal).HasPrecision(10, 2);
            e.HasIndex(x => x.Code).IsUnique();
            // The redemption counter is bumped with a conditional UPDATE; the check keeps a
            // hand-edited row from ever claiming more uses than it allows.
            e.ToTable(t => t.HasCheckConstraint("CK_Promo_Redemptions",
                "[MaxRedemptions] IS NULL OR [Redemptions] <= [MaxRedemptions]"));
        });

        b.Entity<LoyaltyEntry>(e =>
        {
            e.ToTable("LoyaltyEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasConversion<int>();
            e.Property(x => x.Reference).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        });

        // ---------------------------------------------------------------- back office
        b.Entity<TicketType>(e =>
        {
            e.ToTable("TicketTypes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.Property(x => x.NameAz).HasMaxLength(40);
            e.Property(x => x.NameRu).HasMaxLength(40);
            e.Property(x => x.NameTr).HasMaxLength(40);
            e.Property(x => x.PercentOfBase).HasPrecision(5, 2);
            e.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        b.Entity<ConcessionItem>(e =>
        {
            e.ToTable("ConcessionItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Category).HasConversion<int>();
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.CostPrice).HasPrecision(10, 2);
            // A stock count below zero is a sale that should have been refused.
            e.ToTable(t => t.HasCheckConstraint("CK_Concession_Stock", "[Stock] >= 0"));
        });

        b.Entity<ConcessionSale>(e =>
        {
            e.ToTable("ConcessionSales");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(16).IsRequired();
            e.Property(x => x.Tender).HasConversion<int>();
            e.Property(x => x.Total).HasPrecision(10, 2);
            e.Property(x => x.Cost).HasPrecision(10, 2);
            e.HasIndex(x => x.ShiftId);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ConcessionSaleLine>(e =>
        {
            e.ToTable("ConcessionSaleLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Category).HasConversion<int>();
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.UnitCost).HasPrecision(10, 2);
            e.Ignore(x => x.LineTotal);
        });

        b.Entity<CashShift>(e =>
        {
            e.ToTable("CashShifts");
            e.HasKey(x => x.Id);
            e.Property(x => x.CashierName).HasMaxLength(120).IsRequired();
            e.Property(x => x.OpeningFloat).HasPrecision(10, 2);
            e.Property(x => x.ExpectedCash).HasPrecision(10, 2);
            e.Property(x => x.CountedCash).HasPrecision(10, 2);
            e.Property(x => x.Variance).HasPrecision(10, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Ignore(x => x.IsOpen);
            // The "one open shift per cashier" rule, held by the database rather than by a
            // read-then-write that two quick clicks could both pass.
            e.HasIndex(x => x.CashierId).IsUnique().HasFilter("[ClosedAtUtc] IS NULL");
        });

        b.Entity<FilmDeal>(e =>
        {
            e.ToTable("FilmDeals");
            e.HasKey(x => x.Id);
            e.Property(x => x.MovieTitle).HasMaxLength(250).IsRequired();
            e.Property(x => x.Distributor).HasMaxLength(120).IsRequired();
            e.Property(x => x.SharePercent).HasPrecision(5, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => x.MovieId).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        // Payments follow their screening out of sight.
        b.Entity<SeatPayment>().HasQueryFilter(p => !p.Screening!.IsDeleted);

        base.OnModelCreating(b);
    }
}
