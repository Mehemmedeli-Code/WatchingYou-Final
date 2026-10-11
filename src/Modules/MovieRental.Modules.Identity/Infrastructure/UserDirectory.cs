using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Identity.Infrastructure;

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<UserContact?> GetContactAsync(Guid userId, CancellationToken ct = default) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserContact(u.Id, u.FullName, u.Email, u.PhoneNumber, u.ShareOnGlobe))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UserContact>> GetContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, UserContact>();
        var ids = userIds.Distinct().ToList();
        return await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserContact(u.Id, u.FullName, u.Email, u.PhoneNumber, u.ShareOnGlobe))
            .ToDictionaryAsync(u => u.Id, ct);
    }

    public Task<bool> MayViewProfileAsync(Guid viewerId, Guid ownerId, CancellationToken ct = default) =>
        Features.PeopleEndpoints.MayViewAsync(ownerId, db, viewerId, ct);

    public async Task<IReadOnlySet<Guid>> PrivateAccountsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        userIds.Count == 0 ? new HashSet<Guid>()
            : (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsPrivate).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
}
