using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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

/// <summary>A handle as it would be stored (lower case, no @), and whether it is free.</summary>
public sealed record UsernameCheck(string Username, bool Available);

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
                // Names are matched as typed: lower-casing "İlham" gives "i̇lham" (i plus a dot),
                // which matches nobody. The comparison ignores case and accents instead, so
                // "ilham" finds "İlham" and "sevinc" finds "Şevinc".
                var name = (q ?? "").Trim().TrimStart('@');
                if (name.Length > 60) name = name[..60];
                var pageNo = Math.Clamp(page ?? 1, 1, 10_000);
                const int size = 20;

                // An empty box shows nobody: there is no "browse all users" on a site this size.
                // From the first letter on, every keystroke narrows the list.
                if (name.Length == 0) return TypedResults.Ok(new PeoplePage([], pageNo, size, false));

                // Handle matches first (that is what people type), then names; one row more than
                // a page to know whether a next page exists.
                // ponytail: the name half is a LIKE '%x%' scan; add full-text search on FullName
                // when the user table is large enough for it to show.
                var ids = await db.Users.AsNoTracking()
                    .Where(u => !u.IsSuspended &&
                                ((u.Username != null && u.Username.StartsWith(term)) ||
                                 EF.Functions.Collate(u.FullName, "Latin1_General_100_CI_AI").Contains(name)))
                    .OrderBy(u => u.Username != null && u.Username.StartsWith(term) ? 0 : 1)
                    .ThenBy(u => u.Username)
                    .Skip((pageNo - 1) * size).Take(size + 1)
                    .Select(u => u.Id)
                    .ToListAsync(ct);

                var items = await CardsAsync(ids.Take(size).ToList(), db, currentUser.RequireId(), presence, ct);
                return TypedResults.Ok(new PeoplePage(items, pageNo, size, ids.Count > size));
            })
            .WithName("SearchPeople")
            .WithSummary("Find people by @username or name, 20 a page")
            .WithDescription("`q`: one character or more (empty gives an empty page). Usernames that start with it come first, " +
                             "then names that contain it, ignoring case and accents.");

        // The edit-profile box asks while the person types, so "taken" shows before Save. Saving
        // still decides: the unique index on Username is what keeps two people off one handle.
        people.MapGet("/username-available", async Task<Results<Ok<UsernameCheck>, BadRequest<MovieRental.SharedKernel.Results.Error>>> (
                string? username, IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var handle = Usernames.Normalize(username);
                if (!Usernames.IsValid(handle))
                    return TypedResults.BadRequest(MovieRental.SharedKernel.Results.Error.Validation(
                        "Username: 3-30 characters, lower-case letters, digits, dots and underscores."));
                var me = currentUser.RequireId();
                // Suspended accounts keep their handle, so they count as taken too.
                var taken = Usernames.IsReserved(handle) || await db.Users.AnyAsync(u => u.Username == handle && u.Id != me, ct);
                return TypedResults.Ok(new UsernameCheck(handle, !taken));
            })
            .WithName("UsernameAvailable")
            .WithSummary("Is this @username free?")
            .WithDescription("`available` is true when nobody else has it (your own counts as free). Case does not matter: " +
                             "\"Ali\" and \"ali\" are the same handle. 400: not 3-30 of a-z, 0-9, dot and underscore.");

        // A profile by its @handle, the way /u/{username} links to it.
        people.MapGet("/u/{username}", async (string username, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                var handle = Usernames.Normalize(username);
                if (!Usernames.IsValid(handle)) return Results.NotFound(MovieRental.SharedKernel.Results.Error.NotFound("User"));

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
                if (p is null) return Results.NotFound(MovieRental.SharedKernel.Results.Error.NotFound("User"));

                var isMe = p.Id == me;
                return Results.Ok(new ProfileView(p.Id, p.Username, p.FullName, p.AvatarUrl, p.Bio,
                    p.Followers, p.Following, p.IsPrivate, isMe, p.Mine == true, p.Mine == false, p.FollowsMe,
                    presence.IsOnline(p.Id), isMe || !p.IsPrivate || p.Mine == true, !isMe && p.ShareOnGlobe));
            })
            .WithName("GetProfile")
            .WithSummary("Someone's profile by @username")
            .WithDescription("`canView` is false for a private account that has not accepted you: then only the header is shown.")
            .Produces<ProfileView>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);

        // Who follows someone, and whom they follow: the lists behind the two counts, newest
        // first, a page at a time. A private account's lists are for its accepted followers.
        people.MapGet("/{id:guid}/followers", async (Guid id, int? page, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            await ViewRefusalAsync(id, db, currentUser.RequireId(), ct) is { } refused
                ? refused
                : await ListAsync(db.Follows.Where(f => f.FolloweeId == id && f.IsAccepted).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FollowerId),
                    page, db, currentUser, presence, ct))
            .WithName("Followers")
            .WithSummary("Who follows someone, newest first, 30 a page")
            .WithDescription("403: a private account whose followers you are not one of.")
            .Produces<PeoplePage>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status403Forbidden)
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);

        people.MapGet("/{id:guid}/following", async (Guid id, int? page, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            await ViewRefusalAsync(id, db, currentUser.RequireId(), ct) is { } refused
                ? refused
                : await ListAsync(db.Follows.Where(f => f.FollowerId == id && f.IsAccepted).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FolloweeId),
                    page, db, currentUser, presence, ct))
            .WithName("Following")
            .WithSummary("Whom someone follows, newest first, 30 a page")
            .WithDescription("403: a private account whose followers you are not one of.")
            .Produces<PeoplePage>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status403Forbidden)
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);

        people.MapGet("/me", async (IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                return TypedResults.Ok(new FollowCounts(
                    await db.Follows.CountAsync(f => f.FolloweeId == me && f.IsAccepted, ct),
                    await db.Follows.CountAsync(f => f.FollowerId == me && f.IsAccepted, ct),
                    await db.Follows.CountAsync(f => f.FolloweeId == me && !f.IsAccepted, ct)));
            })
            .WithName("MyFollowCounts")
            .WithSummary("My follower, following and waiting-request counts");

        people.MapPost("/{id:guid}/follow", async (Guid id, IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                if (id == me) return Results.BadRequest(MovieRental.SharedKernel.Results.Error.Validation("You cannot follow yourself."));
                var target = await db.Users.AsNoTracking().Where(u => u.Id == id && !u.IsSuspended)
                    .Select(u => new { u.IsPrivate }).FirstOrDefaultAsync(ct);
                if (target is null) return Results.NotFound(MovieRental.SharedKernel.Results.Error.NotFound("User"));

                // The pair is the key: following (or asking) twice is simply already done.
                var existing = await db.Follows.AsNoTracking().FirstOrDefaultAsync(f => f.FollowerId == me && f.FolloweeId == id, ct);
                if (existing is not null) return Results.Ok(new FollowResult(existing.IsAccepted ? "following" : "requested"));

                db.Follows.Add(new Follow { FollowerId = me, FolloweeId = id, IsAccepted = !target.IsPrivate });
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { /* a double click raced itself; the row exists */ }
                return Results.Ok(new FollowResult(target.IsPrivate ? "requested" : "following"));
            })
            .WithName("Follow").RequireRateLimiting(AppPolicies.WriteRateLimit)
            .WithSummary("Follow someone (a private account gets a request)")
            .WithDescription("`status`: following, or requested for a private account. Following twice changes nothing.")
            .Produces<FollowResult>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status400BadRequest)
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);

        // Unfollow, or take back a request that has not been answered.
        people.MapDelete("/{id:guid}/follow", async (Guid id, IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                await db.Follows.Where(f => f.FollowerId == me && f.FolloweeId == id).ExecuteDeleteAsync(ct);
                return Results.NoContent();
            })
            .WithName("Unfollow")
            .WithSummary("Unfollow someone, or take back a request")
            .WithDescription("Always 204, also when you were not following them.")
            .Produces(StatusCodes.Status204NoContent);

        // Requests waiting for me, newest first.
        people.MapGet("/requests", async (int? page, IdentityDbContext db, ICurrentUser currentUser,
                Presence presence, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                return await ListAsync(db.Follows.Where(f => f.FolloweeId == me && !f.IsAccepted)
                    .OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FollowerId), page, db, currentUser, presence, ct);
            })
            .WithName("FollowRequests")
            .WithSummary("Follow requests waiting for me, newest first")
            .Produces<PeoplePage>();

        // Accept: they now follow me. The answer is their card, so the page can offer "Follow back".
        people.MapPost("/requests/{followerId:guid}/accept", async (Guid followerId, IdentityDbContext db,
                ICurrentUser currentUser, Presence presence, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                var accepted = await db.Follows.Where(f => f.FollowerId == followerId && f.FolloweeId == me && !f.IsAccepted)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsAccepted, true), ct);
                if (accepted == 0) return Results.NotFound(MovieRental.SharedKernel.Results.Error.NotFound("Follow request"));
                return Results.Ok((await CardsAsync([followerId], db, me, presence, ct)).FirstOrDefault());
            })
            .WithName("AcceptFollowRequest")
            .WithSummary("Accept someone's follow request")
            .WithDescription("Answers with their card, so the page can offer Follow back. 404: no request from them.")
            .Produces<PersonCard>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);

        people.MapDelete("/requests/{followerId:guid}", async (Guid followerId, IdentityDbContext db,
                ICurrentUser currentUser, CancellationToken ct) =>
            {
                var me = currentUser.RequireId();
                await db.Follows.Where(f => f.FollowerId == followerId && f.FolloweeId == me && !f.IsAccepted).ExecuteDeleteAsync(ct);
                return Results.NoContent();
            })
            .WithName("DeclineFollowRequest")
            .WithSummary("Decline someone's follow request")
            .WithDescription("Always 204, also when there was no request.")
            .Produces(StatusCodes.Status204NoContent);
    }

    /// <summary>Your own lists, a public account's, or a private one you were accepted by.</summary>
    internal static async Task<bool> MayViewAsync(Guid id, IdentityDbContext db, Guid me, CancellationToken ct) =>
        id == me
        || !await db.Users.AnyAsync(u => u.Id == id && u.IsPrivate, ct)
        || await db.Follows.AnyAsync(f => f.FollowerId == me && f.FolloweeId == id && f.IsAccepted, ct);

    /// <summary>The same rule for the follower lists: null when they may be seen.
    /// Otherwise the refusal to send: 404 for nobody by that id, 403 for a private account.</summary>
    private static async Task<IResult?> ViewRefusalAsync(Guid id, IdentityDbContext db, Guid me, CancellationToken ct)
    {
        if (id == me) return null;
        var isPrivate = await db.Users.Where(u => u.Id == id && !u.IsSuspended).Select(u => (bool?)u.IsPrivate).FirstOrDefaultAsync(ct);
        if (isPrivate is null) return Results.NotFound(MovieRental.SharedKernel.Results.Error.NotFound("User"));
        if (isPrivate == false || await db.Follows.AnyAsync(f => f.FollowerId == me && f.FolloweeId == id && f.IsAccepted, ct)) return null;
        return Results.Json(MovieRental.SharedKernel.Results.Error.Forbidden("This account is private. Follow it and wait to be accepted to see its lists."),
            statusCode: StatusCodes.Status403Forbidden);
    }

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
