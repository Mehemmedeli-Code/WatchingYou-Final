using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.SharedKernel.Persistence;

namespace MovieRental.Modules.Catalog.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "catalog";
    public override string Schema => SchemaName;

    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<WatchlistItem> Watchlist => Set<WatchlistItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Movie>(e =>
        {
            e.ToTable("Movies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(280).IsRequired();
            e.Property(x => x.Genre).HasMaxLength(80).IsRequired();
            e.Property(x => x.Director).HasMaxLength(150);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.PosterUrl).HasMaxLength(500);
            e.Property(x => x.TrailerUrl).HasMaxLength(500);
            e.Property(x => x.VideoUrl).HasMaxLength(500);
            e.Property(x => x.DailyPrice).HasPrecision(10, 2);

            // Optimistic concurrency: two admins editing stock at the same time must not
            // silently overwrite each other.
            e.Property<byte[]>("RowVersion").IsRowVersion();

            e.HasIndex(x => x.Slug).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.Genre, x.ReleaseYear });
            e.HasIndex(x => x.Title);

            e.HasMany(x => x.Reviews).WithOne(x => x.Movie!).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Review>(e =>
        {
            e.ToTable("Reviews");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.ToTable(t => t.HasCheckConstraint("CK_Review_Stars", "[Stars] BETWEEN 1 AND 5"));
            e.HasIndex(x => new { x.MovieId, x.UserId }).IsUnique().HasFilter("[IsDeleted] = 0");
        });

        b.Entity<WatchlistItem>(e =>
        {
            e.ToTable("WatchlistItems");
            e.HasKey(x => x.Id);
            // One entry per customer per film; also the index the "is it saved?" lookup uses.
            e.HasIndex(x => new { x.UserId, x.MovieId }).IsUnique();
            e.HasOne(x => x.Movie).WithMany().HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(b);
    }
}
