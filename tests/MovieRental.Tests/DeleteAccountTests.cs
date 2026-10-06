using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Features;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Tests;

/// <summary>
/// Closing your own account — required by both app stores, and irreversible, so worth proving:
/// the wrong password changes nothing, the right one leaves no personal data and no live
/// session behind, and the address can be used to sign up again.
///
/// Real SQL Server, like the seat tests: the soft-delete filter and the filtered unique index
/// on e-mail are database behaviour an in-memory provider would not reproduce.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeleteAccountTests : IAsyncLifetime
{
    private const string Password = "Correct-Horse-9";
    private readonly string _database = $"WatchingYouTests_{Guid.NewGuid():N}";
    private readonly IPasswordHasher _hasher = new BCryptPasswordHasher();

    private IdentityDbContext NewContext() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={_database};Trusted_Connection=True;TrustServerCertificate=True")
        .Options);

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private async Task<Guid> AddUserAsync(string email, string roles = AppRoles.Customer)
    {
        await using var db = NewContext();
        var user = new AppUser
        {
            Email = email, FullName = "Aysel Quliyeva", PhoneNumber = "+994501234567",
            PasswordHash = _hasher.Hash(Password), IsEmailConfirmed = true, Roles = roles,
            ShareOnGlobe = true, City = "Baku", Latitude = 40.4, Longitude = 49.9,
        };
        user.RefreshTokens.Add(new RefreshTokenEntity { Token = Guid.NewGuid().ToString("N"), ExpiresAtUtc = DateTime.UtcNow.AddDays(14) });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private DeleteAccountHandler Handler(IdentityDbContext db, Guid userId) =>
        new(db, new FixedUser(userId), _hasher, new NoAudit(), new HttpContextAccessor());

    [Fact]
    public async Task The_wrong_password_changes_nothing()
    {
        var id = await AddUserAsync("aysel@example.test");

        await using (var db = NewContext())
        {
            var result = await Handler(db, id).Handle(new DeleteAccountCommand("not-it"), default);
            Assert.False(result.IsSuccess);
        }

        await using var check = NewContext();
        var user = await check.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("aysel@example.test", user.Email);
        Assert.False(user.IsDeleted);
    }

    [Fact]
    public async Task The_right_password_erases_the_person_and_ends_every_session()
    {
        var id = await AddUserAsync("aysel@example.test");

        await using (var db = NewContext())
        {
            var result = await Handler(db, id).Handle(new DeleteAccountCommand(Password), default);
            Assert.True(result.IsSuccess);
        }

        await using var check = NewContext();
        Assert.False(await check.Users.AnyAsync(u => u.Id == id));   // gone from every normal query

        var row = await check.Users.IgnoreQueryFilters().Include(u => u.RefreshTokens).SingleAsync(u => u.Id == id);
        Assert.True(row.IsDeleted);
        Assert.DoesNotContain("aysel", row.Email);
        Assert.Equal("Deleted user", row.FullName);
        Assert.Null(row.PhoneNumber);
        Assert.Null(row.City);
        Assert.Null(row.Latitude);
        Assert.False(row.ShareOnGlobe);
        Assert.False(_hasher.Verify(Password, row.PasswordHash));
        Assert.All(row.RefreshTokens, token => Assert.NotNull(token.RevokedAtUtc));

        // The address is free again: the same person can sign up afresh.
        var again = await AddUserAsync("aysel@example.test");
        Assert.NotEqual(id, again);
    }

    [Fact]
    public async Task An_administrator_cannot_delete_their_own_account()
    {
        var id = await AddUserAsync("boss@example.test", $"{AppRoles.Admin},{AppRoles.Customer}");

        await using (var db = NewContext())
        {
            var result = await Handler(db, id).Handle(new DeleteAccountCommand(Password), default);
            Assert.False(result.IsSuccess);
            Assert.Equal("forbidden", result.Error.Code);
        }

        await using var check = NewContext();
        Assert.True(await check.Users.AnyAsync(u => u.Id == id));
    }

    private sealed class FixedUser(Guid id) : ICurrentUser
    {
        public Guid? Id => id;
        public string? Email => null;
        public IReadOnlyList<string> Roles => [];
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public Guid RequireId() => id;
    }

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditRecord>> RecentAsync(int take = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditRecord>>([]);
    }
}
