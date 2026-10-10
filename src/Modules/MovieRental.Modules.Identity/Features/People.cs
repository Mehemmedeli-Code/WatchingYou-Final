using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// The People page: find someone by @username (or name) and follow them.
//
// Built for a big user table: results come a page at a time, an empty search returns nobody,
// and nothing here ever lists "everyone". Only the public handle, name and picture leave the
// server — never an e-mail or a city.
//
// Private accounts approve their followers. Following one leaves a request (Follow.IsAccepted
// false) that the owner accepts or declines; counts and lists only ever include accepted rows.

/// <param name="IsFollowing">The viewer follows them (accepted).</param>
/// <param name="IsRequested">The viewer asked to follow them and is waiting.</param>
/// <param name="FollowsMe">They follow the viewer (accepted) — for "Follow back".</param>
public sealed record PersonCard(
    Guid Id, string? Username, string FullName, string? AvatarUrl,
    int Followers, bool IsFollowing, bool IsMe, bool IsOnline,
    bool IsRequested = false, bool IsPrivate = false, bool FollowsMe = false);

public sealed record PeoplePage(IReadOnlyList<PersonCard> Items, int Page, int PageSize, bool HasMore);

public sealed record FollowCounts(int Followers, int Following, int Requests);

/// <summary>What a follow did: "following" at once, or "requested" for a private account.</summary>
public sealed record FollowResult(string Status);

/// <summary>The top of someone's profile page. <paramref name="CanView"/> is false for a private
/// account the viewer has not been accepted by: the page then shows the header and nothing else.</summary>
public sealed record ProfileView(
    Guid Id, string? Username, string FullName, string? AvatarUrl, string? Bio,
    int Followers, int Following, bool IsPrivate, bool IsMe, bool IsFollowing, bool IsRequested,
    bool FollowsMe, bool IsOnline, bool CanView, bool CanMessage);

public static class PeopleEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var people = app.MapGroup("/api/people").WithTags("People").RequireAuthorization();

        people.MapGet("", async (string? q, int? page, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            {
                var term = Usernames.Normalize(q);
                if (term.Length > 60) term = term[..60];
                var pageNo = Math.Clamp(page ?? 1, 1, 10_000);
                const int size = 20;

                // An empty box shows nobody: there is no "browse all users" on a site this size.
                if (term.Length < 2) return Results.Ok(new PeoplePage([], pageNo, size, false));

                // Handle matches first (that is what people type), then names; one row more than
                // a page to know whether a next page exists.
                // ponytail: the name half is a LIKE '%x%' scan; add full-text search on FullName
                // when the user table is large enough for it to show.
                var ids = await db.Users.AsNoTracking()
                    .Where(u => !u.IsSuspended &&
                                ((u.Username != null && u.Username.StartsWith(term)) || u.FullName.Contains(term)))
                    .OrderBy(u => u.Username != null && u.Username.StartsWith(term) ? 0 : 1)
                    .ThenBy(u => u.Username)
                    .Skip((pageNo - 1) * size).Take(size + 1)
                    .Select(u => u.Id)
                    .ToListAsync(ct);

                var items = await CardsAsync(ids.Take(size).ToList(), db, currentUser.RequireId(), presence, ct);
                return Results.Ok(new PeoplePage(items, pageNo, size, ids.Count > size));
            })
            .WithName("SearchPeople");

        // A profile by its @handle, the way /u/{username} links to it.
        people.MapGet("/u/{username}", async (string username, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                var handle = Usernames.Normalize(username);
                if (!Usernames.IsValid(handle)) return Results.NotFound();

                var p = await db.Users.AsNoTracking()
                    .Where(u => u.Username == handle && !u.IsSuspended)
                    .Select(u => new
                    {
                        u.Id, u.Username, u.FullName, u.AvatarUrl, u.Bio, u.IsPrivate, u.ShareOnGlobe,
                        Followers = db.Follows.Count(f => f.FolloweeId == u.Id && f.IsAccepted),
                        Following = db.Follows.Count(f => f.FollowerId == u.Id && f.IsAccepted),
                        Mine = db.Follows.Where(f => f.FollowerId == me && f.FolloweeId == u.Id).Select(f => (bool?)f.IsAccepted).FirstOrDefault(),
                        FollowsMe = db.Follows.Any(f => f.FollowerId == u.Id && f.FolloweeId == me && f.IsAccepted)
                    })
                    .FirstOrDefaultAsync(ct);
                if (p is null) return Results.NotFound();

                var isMe = p.Id == me;
                return Results.Ok(new ProfileView(p.Id, p.Username, p.FullName, p.AvatarUrl, p.Bio,
                    p.Followers, p.Following, p.IsPrivate, isMe, p.Mine == true, p.Mine == false, p.FollowsMe,
                    presence.IsOnline(p.Id), isMe || !p.IsPrivate || p.Mine == true, !isMe && p.ShareOnGlobe));
            })
            .WithName("GetProfile");

        // Who follows someone, and whom they follow: the lists behind the two counts, newest
        // first, a page at a time. A private account's lists are for its accepted followers.
        people.MapGet("/{id:guid}/followers", async (Guid id, int? page, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            await MayViewAsync(id, db, currentUser.RequireId(), ct)
                ? await ListAsync(db.Follows.Where(f => f.FolloweeId == id && f.IsAccepted).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FollowerId),
                    page, db, currentUser, presence, ct)
                : Results.StatusCode(StatusCodes.Status403Forbidden))
            .WithName("Followers");

        people.MapGet("/{id:guid}/following", async (Guid id, int? page, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            await MayViewAsync(id, db, currentUser.RequireId(), ct)
                ? await ListAsync(db.Follows.Where(f => f.FollowerId == id && f.IsAccepted).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FolloweeId),
                    page, db, currentUser, presence, ct)
                : Results.StatusCode(StatusCodes.Status403Forbidden))
            .WithName("Following");

        people.MapGet("/me", async (IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                return Results.Ok(new FollowCounts(
                    await db.Follows.CountAsync(f => f.FolloweeId == me && f.IsAccepted, ct),
                    await db.Follows.CountAsync(f => f.FollowerId == me && f.IsAccepted, ct),
                    await db.Follows.CountAsync(f => f.FolloweeId == me && !f.IsAccepted, ct)));
            })
            .WithName("MyFollowCounts");

        people.MapPost("/{id:guid}/follow", async (Guid id, IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                if (id == me) return Results.BadRequest(MovieRental.SharedKernel.Results.Error.Validation("You cannot follow yourself."));
                var target = await db.Users.AsNoTracking().Where(u => u.Id == id && !u.IsSuspended)
                    .Select(u => new { u.IsPrivate }).FirstOrDefaultAsync(ct);
                if (target is null) return Results.NotFound();

                // The pair is the key: following (or asking) twice is simply already done.
                var existing = await db.Follows.AsNoTracking().FirstOrDefaultAsync(f => f.FollowerId == me && f.FolloweeId == id, ct);
                if (existing is not null) return Results.Ok(new FollowResult(existing.IsAccepted ? "following" : "requested"));

                db.Follows.Add(new Follow { FollowerId = me, FolloweeId = id, IsAccepted = !target.IsPrivate });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { /* a double click raced itself; the row exists */ }
                return Results.Ok(new FollowResult(target.IsPrivate ? "requested" : "following"));
            })
            .WithName("Follow").RequireRateLimiting(AppPolicies.WriteRateLimit);

        // Unfollow, or take back a request that has not been answered.
        people.MapDelete("/{id:guid}/follow", async (Guid id, IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                await db.Follows.Where(f => f.FollowerId == me && f.FolloweeId == id).ExecuteDeleteAsync(ct);
                return Results.NoContent();
            })
            .WithName("Unfollow");

        // Requests waiting for me, newest first.
        people.MapGet("/requests", async (int? page, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                return await ListAsync(db.Follows.Where(f => f.FolloweeId == me && !f.IsAccepted)
                    .OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FollowerId), page, db, currentUser, presence, ct);
            })
            .WithName("FollowRequests");

        // Accept: they now follow me. The answer is their card, so the page can offer "Follow back".
        people.MapPost("/requests/{followerId:guid}/accept", async (Guid followerId, IdentityDbContext db,
                ICurrentUser currentUser, Presence presence, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                var accepted = await db.Follows.Where(f => f.FollowerId == followerId && f.FolloweeId == me && !f.IsAccepted)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsAccepted, true), ct);
                if (accepted == 0) return Results.NotFound();
                return Results.Ok((await CardsAsync([followerId], db, me, presence, ct)).FirstOrDefault());
            })
            .WithName("AcceptFollowRequest");

        people.MapDelete("/requests/{followerId:guid}", async (Guid followerId, IdentityDbContext db,
                ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                await db.Follows.Where(f => f.FollowerId == followerId && f.FolloweeId == me && !f.IsAccepted).ExecuteDeleteAsync(ct);
                return Results.NoContent();
            })
            .WithName("DeclineFollowRequest");
    }

    /// <summary>Your own lists, a public account's, or a private one you were accepted by.</summary>
    internal static async Task<bool> MayViewAsync(Guid id, IdentityDbContext db, Guid me, CancellationToken ct) =>
        id == me
        || !await db.Users.AnyAsync(u => u.Id == id && u.IsPrivate, ct)
        || await db.Follows.AnyAsync(f => f.FollowerId == me && f.FolloweeId == id && f.IsAccepted, ct);

    private static async Task<IResult> ListAsync(IQueryable<Guid> ids, int? page, IdentityDbContext db,
        ICurrentUser currentUser, Presence presence, CancellationToken ct)
    {
        var pageNo = Math.Clamp(page ?? 1, 1, 10_000);
        const int size = 30;
        var pageIds = await ids.Skip((pageNo - 1) * size).Take(size + 1).ToListAsync(ct);
        var items = await CardsAsync(pageIds.Take(size).ToList(), db, currentUser.RequireId(), presence, ct);
        return Results.Ok(new PeoplePage(items, pageNo, size, pageIds.Count > size));
    }

    /// <summary>Cards for these people, in the order given, each with the viewer's relation to them.</summary>
    private static async Task<List<PersonCard>> CardsAsync(List<Guid> ids, IdentityDbContext db, Guid me,
        Presence presence, CancellationToken ct)
    {
        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && !u.IsSuspended)
            .Select(u => new
            {
                u.Id, u.Username, u.FullName, u.AvatarUrl, u.IsPrivate,
                Followers = db.Follows.Count(f => f.FolloweeId == u.Id && f.IsAccepted),
                Mine = db.Follows.Where(f => f.FollowerId == me && f.FolloweeId == u.Id).Select(f => (bool?)f.IsAccepted).FirstOrDefault(),
                FollowsMe = db.Follows.Any(f => f.FollowerId == u.Id && f.FolloweeId == me && f.IsAccepted)
            })
            .ToListAsync(ct);

        return ids.Select(id => rows.FirstOrDefault(r => r.Id == id)).Where(r => r is not null)
            .Select(r => new PersonCard(r!.Id, r.Username, r.FullName, r.AvatarUrl, r.Followers,
                r.Mine == true, r.Id == me, presence.IsOnline(r.Id), r.Mine == false, r.IsPrivate, r.FollowsMe))
            .ToList();
    }
}
