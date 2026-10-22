using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.SharedKernel.Persistence;

namespace MovieRental.Modules.Media.Persistence;

public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "media";
    public override string Schema => SchemaName;

    public DbSet<ShortFilm> ShortFilms => Set<ShortFilm>();
    public DbSet<SecurityReport> SecurityReports => Set<SecurityReport>();
    public DbSet<SecurityCheckResult> SecurityCheckResults => Set<SecurityCheckResult>();
    public DbSet<SubmissionComment> SubmissionComments => Set<SubmissionComment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ShortFilm>(e =>
        {
            e.ToTable("ShortFilms");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.AuthorName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Synopsis).HasMaxLength(2000);
            e.Property(x => x.StoredFileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120);
            e.Property(x => x.ReviewerNote).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Origin).HasConversion<int>();
            e.Property(x => x.Visibility).HasConversion<int>();

            e.HasIndex(x => new { x.Status, x.ReviewDeadlineUtc });
            e.HasIndex(x => x.UserId);
            // The galleries' only query shape: published films of one origin, newest first.
            e.HasIndex(x => new { x.Status, x.Visibility, x.Origin, x.ApprovedAtUtc });

            e.HasOne(x => x.SecurityReport).WithOne(x => x.ShortFilm!)
             .HasForeignKey<SecurityReport>(x => x.ShortFilmId).OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Comments).WithOne(x => x.ShortFilm!)
             .HasForeignKey(x => x.ShortFilmId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SecurityReport>(e =>
        {
            e.ToTable("SecurityReports");
            e.HasKey(x => x.Id);
            e.Property(x => x.ReviewerName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.Verdict).HasConversion<int>();
            e.HasIndex(x => x.ShortFilmId).IsUnique();

            e.HasMany(x => x.Checks).WithOne(x => x.Report!)
             .HasForeignKey(x => x.SecurityReportId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SecurityCheckResult>(e =>
        {
            e.ToTable("SecurityCheckResults");
            e.HasKey(x => x.Id);
            e.Property(x => x.Check).HasConversion<int>();
            e.Property(x => x.Outcome).HasConversion<int>();
            e.Property(x => x.Note).HasMaxLength(500);
            // One verdict per check per report; a duplicate row would make "did it pass?" ambiguous.
            e.HasIndex(x => new { x.SecurityReportId, x.Check }).IsUnique();
        });

        b.Entity<SubmissionComment>(e =>
        {
            e.ToTable("SubmissionComments");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorName).HasMaxLength(150).IsRequired();
            e.Property(x => x.AuthorRole).HasMaxLength(30).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.ShortFilmId, x.CreatedAtUtc });
        });

        base.OnModelCreating(b);
    }
}
