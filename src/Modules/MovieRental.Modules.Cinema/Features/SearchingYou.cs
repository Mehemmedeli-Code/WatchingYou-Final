using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// SearchingYou: "who wants to come with me?" Someone says what company they want (women, men or
// anyone), for which film, and how many. The owner buys the tickets for the whole group; the people
// who join buy nothing. Either the tickets are bought already — then one person fewer than the
// tickets held can be invited, since one is the owner's — or they will be: then the search lives
// only while the screening still has a seat for everyone in the group, and is called off when it no
// longer does. It is open until the film starts, enough people have joined, or its owner closes it;
// anyone who joined can leave again, which reopens a search that had filled.

public sealed record SearchMember(string Name, string? Username);

public sealed record SearchItem(
    Guid Id, string OwnerName, string? OwnerUsername, string? OwnerAvatarUrl, bool Mine, bool Joined,
    string Partner, int PeopleWanted, int PeopleJoined, bool HasTickets, bool Found, bool Cancelled, int SeatsLeft,
    IReadOnlyList<SearchMember> Members,
    Guid ScreeningId, string MovieTitle, string? PosterUrl, string Venue, string City, string Hall,
    DateTime StartsAtUtc, string AudioLanguage, string? SubtitleLanguage);

public sealed record SearchTicket(
    Guid ScreeningId, string MovieTitle, string? PosterUrl, string Venue, string City, string Hall,
    DateTime StartsAtUtc, string AudioLanguage, string? SubtitleLanguage, int Seats, bool Searching);

internal static class Searches
{
    /// <summary>Not closed by its owner, and the film neither started nor cancelled.</summary>
    public static IQueryable<PartnerRequest> Live(this IQueryable<PartnerRequest> requests, DateTime now) =>
        requests.Where(r => r.ClosedAtUtc == null && r.Screening!.StartsAtUtc > now && !r.Screening.IsCancelled);

    /// <summary>Open to join: live, not called off, places still free.</summary>
    public static IQueryable<PartnerRequest> Open(this IQueryable<PartnerRequest> requests, DateTime now) =>
        requests.Live(now).Where(r => r.CancelledAtUtc == null && r.FilledAtUtc == null && r.Members.Count < r.PeopleWanted);

    /// <summary>
    /// Calls off every search whose tickets are still to be bought when the screening no longer has
    /// a seat for each person in the group still without one: three going, two seats left — off.
    /// The owner buys for the group, so the seats the owner has bought are the group's, and buying
    /// them never calls a search off.
    /// </summary>
    public static async Task<int> CallOffShortAsync(CinemaDbContext db, DateTime now, CancellationToken ct)
    {
        var watched = await db.PartnerRequests.Live(now)
            .Where(r => !r.HasTickets && r.CancelledAtUtc == null)
            .Select(r => new
            {
                r.Id, r.PeopleWanted,
                Free = r.Screening!.Rows * r.Screening.SeatsPerRow - r.Screening.Bookings.Count(),
                Bought = r.Screening.Bookings.Count(b => b.UserId == r.UserId),
            })
            .ToListAsync(ct);
        var off = watched.Where(r => r.Free < r.PeopleWanted + 1 - r.Bought).Select(r => r.Id).ToList();
        if (off.Count == 0) return 0;
        return await db.PartnerRequests.Where(r => off.Contains(r.Id) && r.CancelledAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelledAtUtc, now), ct);
    }
}

public sealed record GetSearchesQuery : IQuery<IReadOnlyList<SearchItem>>;

internal sealed class GetSearchesHandler(CinemaDbContext db, ICurrentUser currentUser, IUserDirectory users, ICatalogApi catalog)
    : IQueryHandler<GetSearchesQuery, IReadOnlyList<SearchItem>>
{
    public async Task<IReadOnlyList<SearchItem>> Handle(GetSearchesQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        // Everyone's open searches; and the caller's own and joined ones until the film starts —
        // found or called off — so the owner sees who came and nobody misses a cancellation.
        var rows = await db.PartnerRequests.AsNoTracking().Live(DateTime.UtcNow)
            .Where(r => (r.CancelledAtUtc == null && r.FilledAtUtc == null && r.Members.Count < r.PeopleWanted)
                     || r.UserId == me || r.Members.Any(m => m.UserId == me))
            .OrderBy(r => r.Screening!.StartsAtUtc)
            .Take(100)
            .Select(r => new
            {
                r.Id, r.UserId, r.Partner, r.PeopleWanted, r.HasTickets, Joined = r.Members.Count, Mine = r.Members.Any(m => m.UserId == me),
                Found = r.FilledAtUtc != null, Cancelled = r.CancelledAtUtc != null,
                MemberIds = r.Members.OrderBy(m => m.CreatedAtUtc).Select(m => m.UserId).ToList(),
                SeatsLeft = r.Screening!.Rows * r.Screening.SeatsPerRow - r.Screening.Bookings.Count(),
                r.ScreeningId, r.Screening.MovieId, r.Screening.MovieTitle, r.Screening.Hall,
                Venue = r.Screening.HallRoom!.Venue!.Name, City = r.Screening.HallRoom.Venue.City,
                r.Screening.StartsAtUtc, r.Screening.AudioLanguage, r.Screening.SubtitleLanguage,
            })
            .ToListAsync(ct);

        // Who joined is told to the owner only.
        var people = await users.GetContactsAsync([.. rows.Select(r => r.UserId).Concat(rows.Where(r => r.UserId == me).SelectMany(r => r.MemberIds)).Distinct()], ct);
        var posters = await catalog.GetPostersAsync([.. rows.Select(r => r.MovieId).Distinct()], ct);
        return [.. rows.Where(r => people.ContainsKey(r.UserId)).Select(r => new SearchItem(
            r.Id, people[r.UserId].FullName, people[r.UserId].Username, people[r.UserId].AvatarUrl, r.UserId == me, r.Mine,
            r.Partner.ToString().ToLowerInvariant(), r.PeopleWanted, r.Joined, r.HasTickets, r.Found, r.Cancelled, Math.Max(0, r.SeatsLeft),
            r.UserId == me ? [.. r.MemberIds.Where(people.ContainsKey).Select(m => new SearchMember(people[m].FullName, people[m].Username))] : [],
            r.ScreeningId, r.MovieTitle, posters.GetValueOrDefault(r.MovieId), r.Venue, r.City, r.Hall,
            r.StartsAtUtc, r.AudioLanguage, r.SubtitleLanguage))];
    }
}

public sealed record GetSearchTicketsQuery : IQuery<IReadOnlyList<SearchTicket>>;

/// <summary>The screenings the caller holds tickets for and that have not started yet.</summary>
internal sealed class GetSearchTicketsHandler(CinemaDbContext db, ICurrentUser currentUser, ICatalogApi catalog)
    : IQueryHandler<GetSearchTicketsQuery, IReadOnlyList<SearchTicket>>
{
    public async Task<IReadOnlyList<SearchTicket>> Handle(GetSearchTicketsQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var now = DateTime.UtcNow;
        var tickets = await db.SeatBookings.AsNoTracking()
            .Where(b => b.UserId == me && b.ConfirmedAtUtc != null && b.Screening!.StartsAtUtc > now && !b.Screening.IsCancelled)
            .GroupBy(b => b.ScreeningId)
            .Select(g => new { ScreeningId = g.Key, Seats = g.Count() })
            .ToListAsync(ct);
        var ids = tickets.Select(t => t.ScreeningId).ToList();
        var shows = await db.Screenings.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.MovieId, s.MovieTitle, s.Hall, Venue = s.HallRoom!.Venue!.Name, City = s.HallRoom.Venue.City, s.StartsAtUtc, s.AudioLanguage, s.SubtitleLanguage })
            .ToDictionaryAsync(s => s.Id, ct);
        var searching = await db.PartnerRequests.AsNoTracking().Open(now)
            .Where(r => r.UserId == me && ids.Contains(r.ScreeningId))
            .Select(r => r.ScreeningId).ToListAsync(ct);
        var posters = await catalog.GetPostersAsync([.. shows.Values.Select(s => s.MovieId).Distinct()], ct);

        return [.. tickets.Select(t => (t, s: shows[t.ScreeningId])).OrderBy(x => x.s.StartsAtUtc).Select(x => new SearchTicket(
            x.s.Id, x.s.MovieTitle, posters.GetValueOrDefault(x.s.MovieId), x.s.Venue, x.s.City, x.s.Hall,
            x.s.StartsAtUtc, x.s.AudioLanguage, x.s.SubtitleLanguage, x.t.Seats, searching.Contains(x.s.Id)))];
    }
}

public sealed record StartSearchCommand(Guid ScreeningId, PartnerKind Partner, int People, bool HasTickets) : ICommand<Result<Guid>>;

internal sealed class StartSearchValidator : AbstractValidator<StartSearchCommand>
{
    public StartSearchValidator()
    {
        RuleFor(x => x.ScreeningId).NotEmpty();
        RuleFor(x => x.Partner).IsInEnum();
        RuleFor(x => x.People).InclusiveBetween(1, 10);
    }
}

internal sealed class StartSearchHandler(CinemaDbContext db, ICurrentUser currentUser) : ICommandHandler<StartSearchCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(StartSearchCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var now = DateTime.UtcNow;
        var show = await db.Screenings.AsNoTracking()
            .Where(s => s.Id == command.ScreeningId && s.StartsAtUtc > now && !s.IsCancelled)
            .Select(s => new
            {
                Free = s.Rows * s.SeatsPerRow - s.Bookings.Count(),
                Mine = s.Bookings.Count(b => b.UserId == me && b.ConfirmedAtUtc != null),
            })
            .FirstOrDefaultAsync(ct);
        if (show is null) return Result.Failure<Guid>(Error.Validation("Pick a film that has not started yet."));

        if (command.HasTickets)
        {
            // One of the tickets is the owner's: three tickets bring two more people.
            if (show.Mine == 0) return Result.Failure<Guid>(Error.Validation("Pick a film you have a ticket for that has not started yet."));
            if (command.People > show.Mine - 1)
                return Result.Failure<Guid>(Error.Validation("You can invite one person fewer than the tickets you hold."));
        }
        else if (show.Free < command.People + 1)
        {
            return Result.Failure<Guid>(Error.Validation("Too few seats are left at this screening for the whole group."));
        }

        var request = new PartnerRequest
        {
            UserId = me, ScreeningId = command.ScreeningId, Partner = command.Partner,
            PeopleWanted = command.People, HasTickets = command.HasTickets,
        };
        db.PartnerRequests.Add(request);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result.Failure<Guid>(Error.Conflict("You are already searching for this screening."));
        }
        return Result.Success(request.Id);
    }
}

public sealed record JoinSearchCommand(Guid RequestId) : ICommand<Result>;

internal sealed class JoinSearchHandler(CinemaDbContext db, ICurrentUser currentUser) : ICommandHandler<JoinSearchCommand, Result>
{
    public async Task<Result> Handle(JoinSearchCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        // Two people taking the last place at once: one at a time per search, so it never overfills.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var name = $"search:{command.RequestId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC sp_getapplock @Resource = {name}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000", ct);

        var request = await db.PartnerRequests.Open(DateTime.UtcNow).Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == command.RequestId, ct);
        if (request is null) return Result.Failure(Error.NotFound("Search"));
        if (request.UserId == me) return Result.Failure(Error.Conflict("This is your own search."));
        if (request.Members.Any(m => m.UserId == me)) return Result.Failure(Error.Conflict("You have already joined."));

        // Counted before the add: EF puts the new row into request.Members as soon as it is added.
        if (request.Members.Count + 1 >= request.PeopleWanted) request.FilledAtUtc = DateTime.UtcNow;   // found
        // Added to the table, not to request.Members: a new row with its Id already set, reached
        // through the collection, was taken for an existing one and updated (0 rows, a 409).
        db.PartnerRequestMembers.Add(new PartnerRequestMember { RequestId = request.Id, UserId = me });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success();
    }
}

public sealed record LeaveSearchCommand(Guid RequestId) : ICommand<Result>;

internal sealed class LeaveSearchHandler(CinemaDbContext db, ICurrentUser currentUser) : ICommandHandler<LeaveSearchCommand, Result>
{
    public async Task<Result> Handle(LeaveSearchCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var name = $"search:{command.RequestId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC sp_getapplock @Resource = {name}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000", ct);

        var request = await db.PartnerRequests.Live(DateTime.UtcNow).Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == command.RequestId, ct);
        var membership = request?.Members.FirstOrDefault(m => m.UserId == me);
        if (request is null || membership is null) return Result.Failure(Error.NotFound("Search"));

        db.PartnerRequestMembers.Remove(membership);
        // A place is free again: a search that had filled opens again, unless its owner has since
        // opened another for the same film (one open search per film).
        if (request.FilledAtUtc is not null && request.CancelledAtUtc is null
            && !await db.PartnerRequests.AnyAsync(r => r.Id != request.Id && r.UserId == request.UserId && r.ScreeningId == request.ScreeningId
                && r.ClosedAtUtc == null && r.FilledAtUtc == null && r.CancelledAtUtc == null, ct))
            request.FilledAtUtc = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success();
    }
}

public sealed record CloseSearchCommand(Guid RequestId) : ICommand<Result>;

internal sealed class CloseSearchHandler(CinemaDbContext db, ICurrentUser currentUser) : ICommandHandler<CloseSearchCommand, Result>
{
    public async Task<Result> Handle(CloseSearchCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var closed = await db.PartnerRequests
            .Where(r => r.Id == command.RequestId && r.UserId == me && r.ClosedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ClosedAtUtc, DateTime.UtcNow), ct);
        return closed == 1 ? Result.Success() : Result.Failure(Error.NotFound("Search"));
    }
}

public static class SearchingYouEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/searching").WithTags("SearchingYou").RequireAuthorization();

        group.MapGet("", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Ask(new GetSearchesQuery(), ct)))
            .WithName("GetSearches").WithSummary("Open searches for company at a screening, soonest film first, with your own and the ones you joined");

        group.MapGet("/tickets", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.Ask(new GetSearchTicketsQuery(), ct)))
            .WithName("GetSearchTickets").WithSummary("Your tickets for screenings that have not started, to search from");

        group.MapPost("", async Task<Results<Ok<Guid>, BadRequest<Error>, Conflict<Error>>> (StartSearchCommand command, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.Send(command, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "conflict" ? TypedResults.Conflict(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("StartSearch").WithSummary("Search for company at a screening")
            .WithDescription("`partner`: Female, Male or Mixed; `people`: 1-10; `hasTickets`: bought already (then at most one fewer " +
                             "than the tickets you hold) or to be bought (then the screening needs a seat for each of the group). " +
                             "400: none of that holds, or the film has started. 409: already searching for it.")
            .RequireRateLimiting(AppPolicies.WriteRateLimit);

        group.MapPost("/{id:guid}/join", async Task<Results<NoContent, NotFound<Error>, Conflict<Error>>> (Guid id, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.Send(new JoinSearchCommand(id), ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.Conflict(result.Error);
            })
            .WithName("JoinSearch").WithSummary("Join someone's search")
            .WithDescription("404: closed, full, called off, or the film has started. 409: your own, or already joined.")
            .RequireRateLimiting(AppPolicies.WriteRateLimit);

        group.MapDelete("/{id:guid}/join", async Task<Results<NoContent, NotFound<Error>>> (Guid id, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.Send(new LeaveSearchCommand(id), ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
            })
            .WithName("LeaveSearch").WithSummary("Leave a search you joined")
            .WithDescription("A search that had filled opens again. 404: you are not in it, or the film has started.")
            .RequireRateLimiting(AppPolicies.WriteRateLimit);

        group.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound<Error>>> (Guid id, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.Send(new CloseSearchCommand(id), ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
            })
            .WithName("CloseSearch").WithSummary("Close your own search")
            .RequireRateLimiting(AppPolicies.WriteRateLimit);
    }
}
