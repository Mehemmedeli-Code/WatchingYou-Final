using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

// "Watch later" — a list the customer keeps for themselves. Nobody else can read it.

public sealed record WatchlistEntry(DateTime AddedAtUtc, MovieListItem Movie);

public sealed record GetWatchlistQuery : IQuery<IReadOnlyList<WatchlistEntry>>;

internal sealed class GetWatchlistHandler(CatalogDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetWatchlistQuery, IReadOnlyList<WatchlistEntry>>
{
    public async Task<IReadOnlyList<WatchlistEntry>> Handle(GetWatchlistQuery query, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        // The join keeps withdrawn titles off the list without deleting the row: if the film
        // comes back, so does the customer's intention to watch it.
        return await db.Watchlist.AsNoTracking()
            .Where(w => w.UserId == userId)
            .Join(db.Movies.AsNoTracking(), w => w.MovieId, m => m.Id, (w, m) => new { w, m })
            .OrderByDescending(x => x.w.CreatedAtUtc)
            .Select(x => new WatchlistEntry(x.w.CreatedAtUtc, new MovieListItem(
                x.m.Id, x.m.Title, x.m.Slug, x.m.Genre, x.m.ReleaseYear, x.m.DurationMinutes,
                x.m.DailyPrice, x.m.AvailableCopies, x.m.TotalCopies, x.m.AverageRating, x.m.ReviewCount,
                x.m.PosterUrl, x.m.IsDeleted, x.m.VideoUrl != null && x.m.VideoUrl != "")))
            .ToListAsync(ct);
    }
}

/// <summary>Just the ids, so a catalogue page can mark its hearts in one small request.</summary>
public sealed record GetWatchlistIdsQuery : IQuery<IReadOnlyList<Guid>>;

internal sealed class GetWatchlistIdsHandler(CatalogDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetWatchlistIdsQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(GetWatchlistIdsQuery query, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        return await db.Watchlist.AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => w.MovieId)
            .ToListAsync(ct);
    }
}

public sealed record AddToWatchlistCommand(Guid MovieId) : ICommand<Result>;

internal sealed class AddToWatchlistHandler(CatalogDbContext db, ICurrentUser currentUser)
    : ICommandHandler<AddToWatchlistCommand, Result>
{
    public const int MaxEntries = 200;

    public async Task<Result> Handle(AddToWatchlistCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        if (!await db.Movies.AnyAsync(m => m.Id == command.MovieId, ct))
            return Result.Failure(Error.NotFound("Movie"));

        // Adding twice is not an error — the heart was pressed, the film is on the list.
        if (await db.Watchlist.AnyAsync(w => w.UserId == userId && w.MovieId == command.MovieId, ct))
            return Result.Success();

        if (await db.Watchlist.CountAsync(w => w.UserId == userId, ct) >= MaxEntries)
            return Result.Failure(Error.Conflict($"Your list is full ({MaxEntries} films). Remove one first."));

        db.Watchlist.Add(new WatchlistItem { UserId = userId, MovieId = command.MovieId });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two tabs pressing the same heart: the unique index let one through, which is
            // exactly the outcome both of them wanted.
        }

        return Result.Success();
    }
}

public sealed record RemoveFromWatchlistCommand(Guid MovieId) : ICommand<Result>;

internal sealed class RemoveFromWatchlistHandler(CatalogDbContext db, ICurrentUser currentUser)
    : ICommandHandler<RemoveFromWatchlistCommand, Result>
{
    public async Task<Result> Handle(RemoveFromWatchlistCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        await db.Watchlist
            .Where(w => w.UserId == userId && w.MovieId == command.MovieId)
            .ExecuteDeleteAsync(ct);
        return Result.Success();
    }
}

public static class WatchlistEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/watchlist").WithTags("Watchlist").RequireAuthorization();

        group.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetWatchlistQuery(), ct)))
            .WithName("GetWatchlist");

        group.MapGet("/ids", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetWatchlistIdsQuery(), ct)))
            .WithName("GetWatchlistIds");

        group.MapPut("/{movieId:guid}", async (Guid movieId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new AddToWatchlistCommand(movieId), ct);
                if (result.IsSuccess) return Results.NoContent();
                return result.Error.Code == "not_found"
                    ? Results.NotFound(result.Error)
                    : Results.Conflict(result.Error);
            })
            .WithName("AddToWatchlist");

        group.MapDelete("/{movieId:guid}", async (Guid movieId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                await dispatcher.Send(new RemoveFromWatchlistCommand(movieId), ct);
                return Results.NoContent();
            })
            .WithName("RemoveFromWatchlist");
    }
}
