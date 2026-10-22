using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Cqrs;

namespace MovieRental.Modules.Cinema.Features;

// What is showing right now, grouped by film. Distinct from the rental catalogue: the same
// film can be on display in three languages and none of that belongs on the rental page.

public sealed record ShowtimeItem(
    Guid ScreeningId, DateTime StartsAtUtc, string Hall, string AudioLanguage,
    string? SubtitleLanguage, decimal SeatPrice, int SeatsLeft);

public sealed record OnDisplayItem(
    Guid MovieId, string MovieTitle, IReadOnlyList<string> Languages, IReadOnlyList<ShowtimeItem> Showtimes);

public readonly record struct OnDisplayFilter(string? Language, DateTime? From, DateTime? To, string? Search);

public sealed record GetOnDisplayQuery(OnDisplayFilter Filter) : IQuery<IReadOnlyList<OnDisplayItem>>;

internal sealed class GetOnDisplayHandler(CinemaDbContext db)
    : IQueryHandler<GetOnDisplayQuery, IReadOnlyList<OnDisplayItem>>
{
    public async Task<IReadOnlyList<OnDisplayItem>> Handle(GetOnDisplayQuery query, CancellationToken ct)
    {
        var filter = query.Filter;
        var from = filter.From ?? DateTime.UtcNow;
        var to = filter.To ?? from.AddDays(14);

        var screenings = db.Screenings.AsNoTracking()
            .Where(s => s.StartsAtUtc >= from && s.StartsAtUtc <= to);

        if (!string.IsNullOrWhiteSpace(filter.Language))
            screenings = screenings.Where(s => s.AudioLanguage == filter.Language);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            screenings = screenings.Where(s => EF.Functions.Like(s.MovieTitle, $"%{term}%"));
        }

        // Seats left comes from the same filtered booking set the seat map uses, so the two
        // screens can never disagree about how full a hall is.
        var rows = await screenings
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new
            {
                s.Id, s.MovieId, s.MovieTitle, s.StartsAtUtc, s.Hall,
                s.AudioLanguage, s.SubtitleLanguage, s.SeatPrice,
                Capacity = s.Rows * s.SeatsPerRow,
                Taken = s.Bookings.Count
            })
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(r => new { r.MovieId, r.MovieTitle })
            .Select(g => new OnDisplayItem(
                g.Key.MovieId,
                g.Key.MovieTitle,
                [.. g.Select(x => x.AudioLanguage).Distinct().Order()],
                [.. g.Select(x => new ShowtimeItem(
                    x.Id, x.StartsAtUtc, x.Hall, x.AudioLanguage, x.SubtitleLanguage,
                    x.SeatPrice, Math.Max(0, x.Capacity - x.Taken)))]))
            .OrderBy(x => x.MovieTitle)];
    }
}

public static class MoviesOnDisplayEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/on-display",
            async ([AsParameters] OnDisplayFilter filter, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetOnDisplayQuery(filter), ct)))
        .WithName("GetMoviesOnDisplay").WithTags("Cinema").AllowAnonymous();
}
