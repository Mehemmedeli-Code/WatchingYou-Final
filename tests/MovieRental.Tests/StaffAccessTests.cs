using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Features;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Tests;

/// <summary>
/// The staff PIN and the owner's rules: five wrong PINs lock the door, the right one opens
/// it, and nobody but the owner makes admins — the owner themself cannot be touched at all.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StaffAccessTests : IAsyncLifetime
{
    private const string Pin = "482913";
    private readonly string _database = $"WatchingYouTests_{Guid.NewGuid():N}";
    private readonly IPasswordHasher _hasher = new BCryptPasswordHasher();

    private IdentityDbContext NewContext() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(TestDb.ConnectionString(_database)).Options);

    public async ValueTask InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private async Task<Guid> AddAsync(string email, string roles, string? pin = Pin)
    {
        await using var db = NewContext();
        var user = new AppUser
        {
            Email = email, FullName = email, PasswordHash = _hasher.Hash("x-Password-1"), IsEmailConfirmed = true,
            Roles = roles, StaffPinHash = pin is null ? null : _hasher.Hash(pin)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user.Id;
    }

    private Task<StaffUnlockResult> UnlockAsync(IdentityDbContext db, Guid id, string pin) =>
        new UnlockStaffHandler(db, new As(id), _hasher, new NoAudit()).Handle(new UnlockStaffCommand(pin), TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_right_pin_unlocks_and_five_wrong_ones_lock()
    {
        var id = await AddAsync("admin@example.test", "Admin,Customer");
        await using var db = NewContext();

        Assert.Equal(StaffUnlockOutcome.Unlocked, (await UnlockAsync(db, id, Pin)).Outcome);
        for (var i = 1; i < StaffPin.MaxFailures; i++)
            Assert.Equal(StaffUnlockOutcome.Wrong, (await UnlockAsync(db, id, "000000")).Outcome);
        Assert.Equal(StaffUnlockOutcome.Locked, (await UnlockAsync(db, id, "000000")).Outcome);
        // Locked means locked: even the right PIN waits out the lockout.
        Assert.Equal(StaffUnlockOutcome.Locked, (await UnlockAsync(db, id, Pin)).Outcome);
    }

    [Fact]
    public async Task A_customer_or_a_staff_member_without_a_pin_does_not_get_in()
    {
        var customer = await AddAsync("c@example.test", "Customer");
        var fresh = await AddAsync("s@example.test", "Security,Customer", pin: null);
        await using var db = NewContext();

        Assert.Equal(StaffUnlockOutcome.NotStaff, (await UnlockAsync(db, customer, Pin)).Outcome);
        Assert.Equal(StaffUnlockOutcome.NoPin, (await UnlockAsync(db, fresh, Pin)).Outcome);
    }

    [Fact]
    public async Task Only_the_owner_makes_admins_and_nobody_touches_the_owner()
    {
        var owner = await AddAsync("owner@example.test", "SuperAdmin,Admin,Customer");
        var admin = await AddAsync("admin@example.test", "Admin,Customer");
        var customer = await AddAsync("c@example.test", "Customer");
        await using var db = NewContext();

        var asAdmin = new SetUserRolesHandler(db, new As(admin, AppRoles.Admin), new NoAudit());
        Assert.False((await asAdmin.Handle(new SetUserRolesCommand(customer, ["Admin"]), TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await asAdmin.Handle(new SetUserRolesCommand(customer, ["Moderator"]), TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await asAdmin.Handle(new SetUserRolesCommand(owner, ["Customer"]), TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await new SetUserSuspensionHandler(db, new As(admin, AppRoles.Admin), new NoAudit())
            .Handle(new SetUserSuspensionCommand(owner, true, "no"), TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await asAdmin.Handle(new SetUserRolesCommand(customer, ["SuperAdmin"]), TestContext.Current.CancellationToken)).IsSuccess);

        var asOwner = new SetUserRolesHandler(db, new As(owner, AppRoles.SuperAdmin, AppRoles.Admin), new NoAudit());
        Assert.True((await asOwner.Handle(new SetUserRolesCommand(customer, ["Admin"]), TestContext.Current.CancellationToken)).IsSuccess);
    }

    private sealed class As(Guid id, params string[] roles) : ICurrentUser
    {
        public Guid? Id => id;
        public string? Email => null;
        public IReadOnlyList<string> Roles => roles;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => roles.Contains(role);
        public Guid RequireId() => id;
    }

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditRecord>> RecentAsync(int take = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditRecord>>([]);
    }
}
