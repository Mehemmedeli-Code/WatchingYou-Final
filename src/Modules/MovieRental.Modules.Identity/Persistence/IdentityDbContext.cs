using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.SharedKernel.Persistence;

namespace MovieRental.Modules.Identity.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "identity";
    public override string Schema => SchemaName;

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<AuditEntryRow> AuditEntries => Set<AuditEntryRow>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<SupportReply> SupportReplies => Set<SupportReply>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(32);
            e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
            e.Property(x => x.Roles).HasMaxLength(200);
            e.HasIndex(x => x.Email).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasMany(x => x.RefreshTokens).WithOne(x => x.User!).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.VerificationCodes).WithOne(x => x.User!).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshTokenEntity>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(256).IsRequired();
            e.Property(x => x.ReplacedByToken).HasMaxLength(256);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.Property(x => x.RevokedReason).HasMaxLength(200);
            e.HasIndex(x => x.Token).IsUnique();
        });

        b.Entity<VerificationCode>(e =>
        {
            e.ToTable("VerificationCodes");
            e.HasKey(x => x.Id);
            e.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.Salt).HasMaxLength(64).IsRequired();
            e.Property(x => x.Channel).HasConversion<int>();
            e.Property(x => x.Purpose).HasConversion<int>();
            e.HasIndex(x => new { x.UserId, x.Purpose, x.Channel, x.SentAtUtc });
        });

        b.Entity<AuditEntryRow>(e =>
        {
            e.ToTable("AuditEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(80).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(250).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ActorName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ActorRoles).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => new { x.Action, x.AtUtc });
        });

        b.Entity<SupportMessage>(e =>
        {
            e.ToTable("SupportMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(160).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.Status).HasConversion<int>();
            // The desk reads oldest-open-first; the customer reads their own newest-first.
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });

            e.HasMany(x => x.Replies).WithOne(x => x.Message!)
             .HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SupportReply>(e =>
        {
            e.ToTable("SupportReplies");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorName).HasMaxLength(150).IsRequired();
            e.Property(x => x.AuthorRole).HasMaxLength(30).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.MessageId, x.CreatedAtUtc });
        });

        base.OnModelCreating(b);
    }
}
