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
            e.HasIndex(x => new { x.UserId, x.Channel, x.SentAtUtc });
        });

        base.OnModelCreating(b);
    }
}
