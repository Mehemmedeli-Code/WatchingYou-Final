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
            .Select(u => new UserContact(u.Id, u.FullName, u.Email, u.PhoneNumber))
            .FirstOrDefaultAsync(ct);
}
