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
    public DbSet<SupportConversation> SupportConversations => Set<SupportConversation>();
    public DbSet<SupportChatMessage> SupportChatMessages => Set<SupportChatMessage>();
    public DbSet<DirectThread> DirectThreads => Set<DirectThread>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<MessageReport> MessageReports => Set<MessageReport>();

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

        b.Entity<SupportConversation>(e =>
        {
            e.ToTable("SupportConversations");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Status).HasConversion<int>();

            // One live thread per customer; a second would split their history in half.
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => new { x.Status, x.LastMessageAtUtc });

            e.HasMany(x => x.Messages).WithOne(x => x.Conversation!)
             .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SupportChatMessage>(e =>
        {
            e.ToTable("SupportChatMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
        });

        // A token, code or message whose owner has been soft-deleted should disappear with
        // them. Without a matching filter the parent is hidden and the child is not, which is
        // the inconsistency EF warns about.
        b.Entity<RefreshTokenEntity>().HasQueryFilter(t => !t.User!.IsDeleted);
        b.Entity<VerificationCode>().HasQueryFilter(c => !c.User!.IsDeleted);
        b.Entity<SupportChatMessage>().HasQueryFilter(m => !m.Conversation!.IsDeleted);

        b.Entity<DirectThread>(e =>
        {
            e.ToTable("DirectThreads");
            e.HasKey(x => x.Id);
            // One thread per pair. The sorted key is what makes this index possible at all.
            e.HasIndex(x => new { x.LowUserId, x.HighUserId }).IsUnique().HasFilter("[IsDeleted] = 0");
            e.HasIndex(x => x.LastMessageAtUtc);
            e.HasMany(x => x.Messages).WithOne(x => x.Thread!)
             .HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DirectMessage>(e =>
        {
            e.ToTable("DirectMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.SenderName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.ThreadId, x.CreatedAtUtc });
        });

        b.Entity<UserBlock>(e =>
        {
            e.ToTable("UserBlocks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.BlockerId, x.BlockedId }).IsUnique();
        });

        b.Entity<MessageReport>(e =>
        {
            e.ToTable("MessageReports");
            e.HasKey(x => x.Id);
            e.Property(x => x.ReporterEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.AboutEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.Quote).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.Handled, x.CreatedAtUtc });
        });

        b.Entity<DirectMessage>().HasQueryFilter(m => !m.Thread!.IsDeleted);

        base.OnModelCreating(b);
    }
}
