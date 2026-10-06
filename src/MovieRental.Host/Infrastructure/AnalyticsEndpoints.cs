using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// Feeds the charts on the admin dashboard. The host owns no data of its own — it only
/// asks each module for its slice and hands the three series back in display order.
/// </summary>
public static class AnalyticsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/analytics/overview",
            async (IRentalAnalytics rentals, ICatalogAnalytics catalog, ICinemaAnalytics cinema, CancellationToken ct) =>
            {
                // Awaited one by one: each is a small query, and running them in parallel would
                // open several connections for a page one admin looks at.
                var mostRented = await rentals.MostRentedAsync(6, ct);
                var byGenre = await rentals.RentalsByGenreAsync(6, ct);
                var topRated = await catalog.TopRatedAsync(6, ct);
                var revenue = await cinema.RevenueByDayAsync(10, ct);
                var occupancy = await cinema.OccupancyAsync(6, ct);
                var topFilms = await cinema.TopFilmsAsync(6, ct);

                return Results.Ok(new[] { revenue, occupancy, topFilms, mostRented, byGenre, topRated });
            })
            .WithName("GetDashboardAnalytics").WithTags("Analytics").RequireAuthorization(AppRoles.Admin);

        app.MapGet("/api/admin/analytics/cinema-totals",
            async (ICinemaTotals cinema, CancellationToken ct) => Results.Ok(await cinema.TotalsAsync(ct)))
            .WithName("GetCinemaTotals").WithTags("Analytics").RequireAuthorization(AppRoles.Admin);
    }
}
