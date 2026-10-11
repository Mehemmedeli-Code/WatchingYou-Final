using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// Feature 9 — the seat map behind the booking screen.
public sealed record ScreeningListItem(
    Guid Id, Guid MovieId, string MovieTitle, string Hall, DateTime StartsAtUtc,
    decimal SeatPrice, int Capacity, int SeatsTaken,
    string AudioLanguage, string? SubtitleLanguage,
    Guid HallId, string VenueName);

public sealed record SeatState(int Row, int Number, bool IsTaken, bool IsMine, bool IsHeld);

public sealed record SeatMapResponse(
    Guid ScreeningId, string MovieTitle, string Hall, DateTime StartsAtUtc,
    int Rows, int SeatsPerRow, decimal SeatPrice,
    string AudioLanguage, string? SubtitleLanguage,
    Guid HallId, string VenueName, IReadOnlyList<SeatState> Seats);

public sealed record GetScreeningsQuery : IQuery<IReadOnlyList<ScreeningListItem>>;

internal sealed class GetScreeningsHandler(CinemaDbContext db)
    : IQueryHandler<GetScreeningsQuery, IReadOnlyList<ScreeningListItem>>
{
    public async Task<IReadOnlyList<ScreeningListItem>> Handle(GetScreeningsQuery query, CancellationToken ct) =>
        await db.Screenings.AsNoTracking()
            // Only shows still on sale: the page picks the first one, and listing shows that had
            // started (or were cancelled) opened it on a seat map nobody could click.
            .Where(s => s.StartsAtUtc > DateTime.UtcNow && !s.IsCancelled)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new ScreeningListItem(
                s.Id, s.MovieId, s.MovieTitle, s.Hall, s.StartsAtUtc, s.SeatPrice,
                s.Rows * s.SeatsPerRow, s.Bookings.Count, s.AudioLanguage, s.SubtitleLanguage,
                s.HallId, s.HallRoom!.Venue!.Name))
            .ToListAsync(ct);
}

public sealed record GetSeatMapQuery(Guid ScreeningId) : IQuery<SeatMapResponse?>;

internal sealed class GetSeatMapHandler(CinemaDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetSeatMapQuery, SeatMapResponse?>
{
    public async Task<SeatMapResponse?> Handle(GetSeatMapQuery query, CancellationToken ct)
    {
        var screening = await db.Screenings
            .Include(s => s.HallRoom!).ThenInclude(h => h.Venue).AsNoTracking()
            .Include(s => s.Bookings).ThenInclude(b => b.Payment)
            .FirstOrDefaultAsync(s => s.Id == query.ScreeningId, ct);

        if (screening is null) return null;

        var me = currentUser.Id;

        // A hold whose window has passed is not occupying anything, even if the row is still
        // there — the sweeper deletes it within the minute, and the map should not lie in
        // the meantime.
        var live = screening.Bookings
            // The payment's own expiry, not a fixed 15 minutes: Stripe holds last 45, and a seat
            // shown free while still held only fails later, at checkout.
            .Where(b => b.ConfirmedAtUtc is not null || b.Payment is null || b.Payment.ExpiresAtUtc > DateTime.UtcNow)
            .ToDictionary(b => (b.Row, b.Number), b => b);

        // The full grid is materialised server-side so the client renders one array
        // instead of reconciling a sparse booking list against seat geometry.
        var seats = new List<SeatState>(screening.Capacity);
        for (var row = 1; row <= screening.Rows; row++)
        for (var number = 1; number <= screening.SeatsPerRow; number++)
        {
            var occupied = live.TryGetValue((row, number), out var booking);
            var isHeld = occupied && booking!.ConfirmedAtUtc is null;

            seats.Add(new SeatState(
                Row: row,
                Number: number,
                IsTaken: occupied,
                // "Mine" means a ticket exists. An unfinished checkout is shown as held, not
                // owned, so nobody thinks they already have seats they have not confirmed.
                IsMine: occupied && !isHeld && me is not null && booking!.UserId == me,
                IsHeld: isHeld));
        }

        return new SeatMapResponse(screening.Id, screening.MovieTitle, screening.Hall,
            screening.StartsAtUtc, screening.Rows, screening.SeatsPerRow, screening.SeatPrice,
            screening.AudioLanguage, screening.SubtitleLanguage,
            screening.HallId, screening.HallRoom?.Venue?.Name ?? "", seats);
    }
}

public static class SeatMapEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/screenings", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetScreeningsQuery(), ct)))
            .WithName("GetScreenings").WithTags("Cinema").AllowAnonymous().CacheOutput(AppPolicies.CinemaCache);

        app.MapGet("/api/screenings/{id:guid}/seats",
            async Task<Results<Ok<SeatMapResponse>, NotFound>> (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var map = await dispatcher.Ask(new GetSeatMapQuery(id), ct);
                return map is null ? TypedResults.NotFound() : TypedResults.Ok(map);
            })
        .WithName("GetSeatMapWithId").WithTags("Cinema").AllowAnonymous();
    }
}
