using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
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

// ---- Reads ----

public sealed record SearchPeopleQuery(string? Q, int? Page) : IQuery<PeoplePage>;

internal sealed class SearchPeopleHandler(IdentityDbContext db, ICurrentUser currentUser, Presence presence)
    : IQueryHandler<SearchPeopleQuery, PeoplePage>
{
    public async Task<PeoplePage> Handle(SearchPeopleQuery query, CancellationToken ct)
    {
        var term = Usernames.Normalize(query.Q);
        if (term.Length > 60) term = term[..60];
        // Names are matched as typed: lower-casing "İlham" gives "i̇lham" (i plus a dot),
        // which matches nobody. The comparison ignores case and accents instead, so
        // "ilham" finds "İlham" and "sevinc" finds "Şevinc".
        var name = (query.Q ?? "").Trim().TrimStart('@');
        if (name.Length > 60) name = name[..60];
        var pageNo = Math.Clamp(query.Page ?? 1, 1, 10_000);
        const int size = 20;

        // An empty box shows nobody: there is no "browse all users" on a site this size.
        // From the first letter on, every keystroke narrows the list.
        if (name.Length == 0) return new PeoplePage([], pageNo, size, false);

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

        var items = await PeopleReads.CardsAsync(ids.Take(size).ToList(), db, currentUser.RequireId(), presence, ct);
        return new PeoplePage(items, pageNo, size, ids.Count > size);
    }
}

public sealed record UsernameAvailableQuery(string? Username) : IQuery<Result<UsernameCheck>>;

internal sealed class UsernameAvailableHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<UsernameAvailableQuery, Result<UsernameCheck>>
{
    public async Task<Result<UsernameCheck>> Handle(UsernameAvailableQuery query, CancellationToken ct)
    {
        var handle = Usernames.Normalize(query.Username);
        if (!Usernames.IsValid(handle))
            return Result.Failure<UsernameCheck>(Error.Validation("Username: 3-30 characters, lower-case letters, digits, dots and underscores."));
        var me = currentUser.RequireId();
        // Suspended accounts keep their handle, so they count as taken too.
        var taken = Usernames.IsReserved(handle) || await db.Users.AnyAsync(u => u.Username == handle && u.Id != me, ct);
        return new UsernameCheck(handle, !taken);
    }
}

public sealed record GetProfileQuery(string Username) : IQuery<ProfileView?>;

internal sealed class GetProfileHandler(IdentityDbContext db, ICurrentUser currentUser, Presence presence)
    : IQueryHandler<GetProfileQuery, ProfileView?>
{
    public async Task<ProfileView?> Handle(GetProfileQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var handle = Usernames.Normalize(query.Username);
        if (!Usernames.IsValid(handle)) return null;

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
        if (p is null) return null;

        var isMe = p.Id == me;
        return new ProfileView(p.Id, p.Username, p.FullName, p.AvatarUrl, p.Bio,
            p.Followers, p.Following, p.IsPrivate, isMe, p.Mine == true, p.Mine == false, p.FollowsMe,
            presence.IsOnline(p.Id), isMe || !p.IsPrivate || p.Mine == true, !isMe && p.ShareOnGlobe);
    }
}

/// <param name="Followers">True: who follows them; false: whom they follow.</param>
public sealed record FollowListQuery(Guid Id, int? Page, bool Followers) : IQuery<Result<PeoplePage>>;

internal sealed class FollowListHandler(IdentityDbContext db, ICurrentUser currentUser, Presence presence)
    : IQueryHandler<FollowListQuery, Result<PeoplePage>>
{
    public async Task<Result<PeoplePage>> Handle(FollowListQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        if (await PeopleReads.ViewRefusalAsync(query.Id, db, me, ct) is { } refused) return Result.Failure<PeoplePage>(refused);
        var ids = query.Followers
            ? db.Follows.Where(f => f.FolloweeId == query.Id && f.IsAccepted).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FollowerId)
            : db.Follows.Where(f => f.FollowerId == query.Id && f.IsAccepted).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FolloweeId);
        return await PeopleReads.ListAsync(ids, query.Page, db, me, presence, ct);
    }
}

public sealed record MyFollowCountsQuery : IQuery<FollowCounts>;

internal sealed class MyFollowCountsHandler(IdentityDbContext db, ICurrentUser currentUser) : IQueryHandler<MyFollowCountsQuery, FollowCounts>
{
    public async Task<FollowCounts> Handle(MyFollowCountsQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        return new FollowCounts(
            await db.Follows.CountAsync(f => f.FolloweeId == me && f.IsAccepted, ct),
            await db.Follows.CountAsync(f => f.FollowerId == me && f.IsAccepted, ct),
            await db.Follows.CountAsync(f => f.FolloweeId == me && !f.IsAccepted, ct));
    }
}

/// <summary>Requests waiting for me, newest first.</summary>
public sealed record FollowRequestsQuery(int? Page) : IQuery<PeoplePage>;

internal sealed class FollowRequestsHandler(IdentityDbContext db, ICurrentUser currentUser, Presence presence)
    : IQueryHandler<FollowRequestsQuery, PeoplePage>
{
    public Task<PeoplePage> Handle(FollowRequestsQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        return PeopleReads.ListAsync(db.Follows.Where(f => f.FolloweeId == me && !f.IsAccepted)
            .OrderByDescending(f => f.CreatedAtUtc).Select(f => f.FollowerId), query.Page, db, me, presence, ct);
    }
}

// ---- Writes ----

public sealed record FollowCommand(Guid Id) : ICommand<Result<FollowResult>>;

internal sealed class FollowHandler(IdentityDbContext db, ICurrentUser currentUser) : ICommandHandler<FollowCommand, Result<FollowResult>>
{
    public async Task<Result<FollowResult>> Handle(FollowCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        if (command.Id == me) return Result.Failure<FollowResult>(Error.Validation("You cannot follow yourself."));
        var target = await db.Users.AsNoTracking().Where(u => u.Id == command.Id && !u.IsSuspended)
            .Select(u => new { u.IsPrivate }).FirstOrDefaultAsync(ct);
        if (target is null) return Result.Failure<FollowResult>(Error.NotFound("User"));

        // The pair is the key: following (or asking) twice is simply already done.
        var existing = await db.Follows.AsNoTracking().FirstOrDefaultAsync(f => f.FollowerId == me && f.FolloweeId == command.Id, ct);
        if (existing is not null) return new FollowResult(existing.IsAccepted ? "following" : "requested");

        db.Follows.Add(new Follow { FollowerId = me, FolloweeId = command.Id, IsAccepted = !target.IsPrivate });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* a double click raced itself; the row exists */ }
        return new FollowResult(target.IsPrivate ? "requested" : "following");
    }
}

/// <summary>Unfollow, or take back a request that has not been answered.</summary>
public sealed record UnfollowCommand(Guid Id) : ICommand<Result>;

internal sealed class UnfollowHandler(IdentityDbContext db, ICurrentUser currentUser) : ICommandHandler<UnfollowCommand, Result>
{
    public async Task<Result> Handle(UnfollowCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        await db.Follows.Where(f => f.FollowerId == me && f.FolloweeId == command.Id).ExecuteDeleteAsync(ct);
        return Result.Success();
    }
}

/// <summary>Accept: they now follow me. The answer is their card, so the page can offer "Follow back".</summary>
public sealed record AcceptFollowRequestCommand(Guid FollowerId) : ICommand<Result<PersonCard?>>;

internal sealed class AcceptFollowRequestHandler(IdentityDbContext db, ICurrentUser currentUser, Presence presence)
    : ICommandHandler<AcceptFollowRequestCommand, Result<PersonCard?>>
{
    public async Task<Result<PersonCard?>> Handle(AcceptFollowRequestCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var accepted = await db.Follows.Where(f => f.FollowerId == command.FollowerId && f.FolloweeId == me && !f.IsAccepted)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsAccepted, true), ct);
        if (accepted == 0) return Result.Failure<PersonCard?>(Error.NotFound("Follow request"));
        return Result.Success((await PeopleReads.CardsAsync([command.FollowerId], db, me, presence, ct)).FirstOrDefault());
    }
}

public sealed record DeclineFollowRequestCommand(Guid FollowerId) : ICommand<Result>;

internal sealed class DeclineFollowRequestHandler(IdentityDbContext db, ICurrentUser currentUser) : ICommandHandler<DeclineFollowRequestCommand, Result>
{
    public async Task<Result> Handle(DeclineFollowRequestCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        await db.Follows.Where(f => f.FollowerId == command.FollowerId && f.FolloweeId == me && !f.IsAccepted).ExecuteDeleteAsync(ct);
        return Result.Success();
    }
}

public static class PeopleEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var people = app.MapGroup("/api/people").WithTags("People").RequireAuthorization();

        people.MapGet("", async (string? q, int? page, IDispatcher d, CancellationToken ct) =>
                TypedResults.Ok(await d.Ask(new SearchPeopleQuery(q, page), ct)))
            .WithName("SearchPeople")
            .WithSummary("Find people by @username or name, 20 a page")
            .WithDescription("`q`: one character or more (empty gives an empty page). Usernames that start with it come first, " +
                             "then names that contain it, ignoring case and accents.");

        // The edit-profile box asks while the person types, so "taken" shows before Save. Saving
        // still decides: the unique index on Username is what keeps two people off one handle.
        people.MapGet("/username-available", async Task<Results<Ok<UsernameCheck>, BadRequest<Error>>> (
                string? username, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.Ask(new UsernameAvailableQuery(username), ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.BadRequest(result.Error);
            })
            .WithName("UsernameAvailable")
            .WithSummary("Is this @username free?")
            .WithDescription("`available` is true when nobody else has it (your own counts as free). Case does not matter: " +
                             "\"Ali\" and \"ali\" are the same handle. 400: not 3-30 of a-z, 0-9, dot and underscore.");

        // A profile by its @handle, the way /u/{username} links to it.
        people.MapGet("/u/{username}", async (string username, IDispatcher d, CancellationToken ct) =>
                await d.Ask(new GetProfileQuery(username), ct) is { } profile
                    ? Results.Ok(profile)
                    : Results.NotFound(Error.NotFound("User")))
            .WithName("GetProfile")
            .WithSummary("Someone's profile by @username")
            .WithDescription("`canView` is false for a private account that has not accepted you: then only the header is shown.")
            .Produces<ProfileView>()
            .Produces<Error>(StatusCodes.Status404NotFound);

        // Who follows someone, and whom they follow: the lists behind the two counts, newest
        // first, a page at a time. A private account's lists are for its accepted followers.
        people.MapGet("/{id:guid}/followers", async (Guid id, int? page, IDispatcher d, CancellationToken ct) =>
                (await d.Ask(new FollowListQuery(id, page, Followers: true), ct)).ToHttp())
            .WithName("Followers")
            .WithSummary("Who follows someone, newest first, 30 a page")
            .WithDescription("403: a private account whose followers you are not one of.")
            .Produces<PeoplePage>()
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status404NotFound);

        people.MapGet("/{id:guid}/following", async (Guid id, int? page, IDispatcher d, CancellationToken ct) =>
                (await d.Ask(new FollowListQuery(id, page, Followers: false), ct)).ToHttp())
            .WithName("Following")
            .WithSummary("Whom someone follows, newest first, 30 a page")
            .WithDescription("403: a private account whose followers you are not one of.")
            .Produces<PeoplePage>()
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status404NotFound);

        people.MapGet("/me", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Ask(new MyFollowCountsQuery(), ct)))
            .WithName("MyFollowCounts")
            .WithSummary("My follower, following and waiting-request counts");

        people.MapPost("/{id:guid}/follow", async (Guid id, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new FollowCommand(id), ct)).ToHttp())
            .WithName("Follow").RequireRateLimiting(AppPolicies.WriteRateLimit)
            .WithSummary("Follow someone (a private account gets a request)")
            .WithDescription("`status`: following, or requested for a private account. Following twice changes nothing.")
            .Produces<FollowResult>()
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status404NotFound);

        people.MapDelete("/{id:guid}/follow", async (Guid id, IDispatcher d, CancellationToken ct) =>
            {
                await d.Send(new UnfollowCommand(id), ct);
                return Results.NoContent();
            })
            .WithName("Unfollow")
            .WithSummary("Unfollow someone, or take back a request")
            .WithDescription("Always 204, also when you were not following them.")
            .Produces(StatusCodes.Status204NoContent);

        people.MapGet("/requests", async (int? page, IDispatcher d, CancellationToken ct) =>
                Results.Ok(await d.Ask(new FollowRequestsQuery(page), ct)))
            .WithName("FollowRequests")
            .WithSummary("Follow requests waiting for me, newest first")
            .Produces<PeoplePage>();

        people.MapPost("/requests/{followerId:guid}/accept", async (Guid followerId, IDispatcher d, CancellationToken ct) =>
                (await d.Send(new AcceptFollowRequestCommand(followerId), ct)).ToHttp())
            .WithName("AcceptFollowRequest")
            .WithSummary("Accept someone's follow request")
            .WithDescription("Answers with their card, so the page can offer Follow back. 404: no request from them.")
            .Produces<PersonCard>()
            .Produces<Error>(StatusCodes.Status404NotFound);

        people.MapDelete("/requests/{followerId:guid}", async (Guid followerId, IDispatcher d, CancellationToken ct) =>
            {
                await d.Send(new DeclineFollowRequestCommand(followerId), ct);
                return Results.NoContent();
            })
            .WithName("DeclineFollowRequest")
            .WithSummary("Decline someone's follow request")
            .WithDescription("Always 204, also when there was no request.")
            .Produces(StatusCodes.Status204NoContent);
    }
}

/// <summary>Reads shared by the People handlers.</summary>
internal static class PeopleReads
{
    /// <summary>Your own lists, a public account's, or a private one you were accepted by.</summary>
    internal static async Task<bool> MayViewAsync(Guid id, IdentityDbContext db, Guid me, CancellationToken ct) =>
        id == me
        || !await db.Users.AnyAsync(u => u.Id == id && u.IsPrivate, ct)
        || await db.Follows.AnyAsync(f => f.FollowerId == me && f.FolloweeId == id && f.IsAccepted, ct);

    /// <summary>The same rule for the follower lists: null when they may be seen. Otherwise why
    /// not: not found for nobody by that id, forbidden for a private account.</summary>
    internal static async Task<Error?> ViewRefusalAsync(Guid id, IdentityDbContext db, Guid me, CancellationToken ct)
    {
        if (id == me) return null;
        var isPrivate = await db.Users.Where(u => u.Id == id && !u.IsSuspended).Select(u => (bool?)u.IsPrivate).FirstOrDefaultAsync(ct);
        if (isPrivate is null) return Error.NotFound("User");
        if (isPrivate == false || await db.Follows.AnyAsync(f => f.FollowerId == me && f.FolloweeId == id && f.IsAccepted, ct)) return null;
        return Error.Forbidden("This account is private. Follow it and wait to be accepted to see its lists.");
    }

    internal static async Task<PeoplePage> ListAsync(IQueryable<Guid> ids, int? page, IdentityDbContext db,
        Guid me, Presence presence, CancellationToken ct)
    {
        var pageNo = Math.Clamp(page ?? 1, 1, 10_000);
        const int size = 30;
        var pageIds = await ids.Skip((pageNo - 1) * size).Take(size + 1).ToListAsync(ct);
        var items = await CardsAsync(pageIds.Take(size).ToList(), db, me, presence, ct);
        return new PeoplePage(items, pageNo, size, pageIds.Count > size);
    }

    /// <summary>Cards for these people, in the order given, each with the viewer's relation to them.</summary>
    internal static async Task<List<PersonCard>> CardsAsync(List<Guid> ids, IdentityDbContext db, Guid me,
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
