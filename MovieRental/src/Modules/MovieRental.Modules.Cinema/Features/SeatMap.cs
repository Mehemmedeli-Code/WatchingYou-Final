using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// Feature 9 — the seat map behind the booking screen.
public sealed record ScreeningListItem(
    Guid Id, Guid MovieId, string MovieTitle, string Hall, DateTime StartsAtUtc,
    decimal SeatPrice, int Capacity, int SeatsTaken);

public sealed record SeatState(int Row, int Number, bool IsTaken, bool IsMine);

public sealed record SeatMapResponse(
    Guid ScreeningId, string MovieTitle, string Hall, DateTime StartsAtUtc,
    int Rows, int SeatsPerRow, decimal SeatPrice, IReadOnlyList<SeatState> Seats);

public sealed record GetScreeningsQuery : IQuery<IReadOnlyList<ScreeningListItem>>;

internal sealed class GetScreeningsHandler(CinemaDbContext db)
    : IQueryHandler<GetScreeningsQuery, IReadOnlyList<ScreeningListItem>>
{
    public async Task<IReadOnlyList<ScreeningListItem>> Handle(GetScreeningsQuery query, CancellationToken ct) =>
        await db.Screenings.AsNoTracking()
            .Where(s => s.StartsAtUtc > DateTime.UtcNow.AddHours(-2))
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new ScreeningListItem(
                s.Id, s.MovieId, s.MovieTitle, s.Hall, s.StartsAtUtc, s.SeatPrice,
                s.Rows * s.SeatsPerRow, s.Bookings.Count))
            .ToListAsync(ct);
}

public sealed record GetSeatMapQuery(Guid ScreeningId) : IQuery<SeatMapResponse?>;

internal sealed class GetSeatMapHandler(CinemaDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetSeatMapQuery, SeatMapResponse?>
{
    public async Task<SeatMapResponse?> Handle(GetSeatMapQuery query, CancellationToken ct)
    {
        var screening = await db.Screenings.AsNoTracking()
            .Include(s => s.Bookings)
            .FirstOrDefaultAsync(s => s.Id == query.ScreeningId, ct);

        if (screening is null) return null;

        var me = currentUser.Id;
        var taken = screening.Bookings.ToDictionary(b => (b.Row, b.Number), b => b.UserId);

        // The full grid is materialised server-side so the client renders one array
        // instead of reconciling a sparse booking list against seat geometry.
        var seats = new List<SeatState>(screening.Capacity);
        for (var row = 1; row <= screening.Rows; row++)
        for (var number = 1; number <= screening.SeatsPerRow; number++)
        {
            var isTaken = taken.TryGetValue((row, number), out var owner);
            seats.Add(new SeatState(row, number, isTaken, isTaken && me is not null && owner == me));
        }

        return new SeatMapResponse(screening.Id, screening.MovieTitle, screening.Hall,
            screening.StartsAtUtc, screening.Rows, screening.SeatsPerRow, screening.SeatPrice, seats);
    }
}

public static class SeatMapEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/screenings", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetScreeningsQuery(), ct)))
            .WithName("GetScreenings").WithTags("Cinema").AllowAnonymous();

        app.MapGet("/api/screenings/{id:guid}/seats",
            async Task<Results<Ok<SeatMapResponse>, NotFound>> (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var map = await dispatcher.Ask(new GetSeatMapQuery(id), ct);
                return map is null ? TypedResults.NotFound() : TypedResults.Ok(map);
            })
        .WithName("GetSeatMapWithId").WithTags("Cinema").AllowAnonymous();
    }
}
