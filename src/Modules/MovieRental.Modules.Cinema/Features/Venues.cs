using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

/// <summary>Everything the 3D preview needs to rebuild a room. Sent as one object so the
/// viewer never has to guess a dimension or fall back on a default that is wrong for
/// this hall.</summary>
public sealed record HallGeometry(
    Guid Id, string Name, string? Format,
    int Rows, int SeatsPerRow, int BlockColumns,
    double AisleWidth, double RowPitch, double RowRise, double SeatWidth,
    double RoomWidth, double RoomDepth, double RoomHeight,
    double ScreenWidth, double ScreenHeight, double ScreenCurveRadius,
    double FirstRowDistance);

public sealed record VenueItem(
    Guid Id, string Name, string City, string? Address,
    double Latitude, double Longitude, IReadOnlyList<HallGeometry> Halls);

public sealed record GetVenuesQuery : IQuery<IReadOnlyList<VenueItem>>;

internal sealed class GetVenuesHandler(CinemaDbContext db) : IQueryHandler<GetVenuesQuery, IReadOnlyList<VenueItem>>
{
    public async Task<IReadOnlyList<VenueItem>> Handle(GetVenuesQuery query, CancellationToken ct)
    {
        var venues = await db.Venues.AsNoTracking()
            .Include(v => v.Halls)
            .OrderBy(v => v.Name)
            .ToListAsync(ct);

        return [.. venues.Select(v => new VenueItem(v.Id, v.Name, v.City, v.Address,
            v.Latitude, v.Longitude, [.. v.Halls.OrderBy(h => h.Name).Select(Map)]))];
    }

    internal static HallGeometry Map(Domain.Hall h) => new(
        h.Id, h.Name, h.Format, h.Rows, h.SeatsPerRow, h.BlockColumns,
        h.AisleWidth, h.RowPitch, h.RowRise, h.SeatWidth,
        h.RoomWidth, h.RoomDepth, h.RoomHeight,
        h.ScreenWidth, h.ScreenHeight, h.ScreenCurveRadius, h.FirstRowDistance);
}

public sealed record GetHallQuery(Guid HallId) : IQuery<HallGeometry?>;

internal sealed class GetHallHandler(CinemaDbContext db) : IQueryHandler<GetHallQuery, HallGeometry?>
{
    public async Task<HallGeometry?> Handle(GetHallQuery query, CancellationToken ct)
    {
        var hall = await db.Halls.AsNoTracking().FirstOrDefaultAsync(h => h.Id == query.HallId, ct);
        return hall is null ? null : GetVenuesHandler.Map(hall);
    }
}

public static class VenueEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/venues", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetVenuesQuery(), ct)))
            .WithName("GetVenues").WithTags("Cinema").AllowAnonymous().CacheOutput(AppPolicies.VenueCache);

        app.MapGet("/api/halls/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var hall = await dispatcher.Ask(new GetHallQuery(id), ct);
                return hall is null ? Results.NotFound() : Results.Ok(hall);
            })
            .WithName("GetHallGeometryWithId").WithTags("Cinema").AllowAnonymous().CacheOutput(AppPolicies.VenueCache);
    }
}
