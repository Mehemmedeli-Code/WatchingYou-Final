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
            async (IRentalAnalytics rentals, ICatalogAnalytics catalog, CancellationToken ct) =>
            {
                var mostRented = await rentals.MostRentedAsync(6, ct);
                var byGenre = await rentals.RentalsByGenreAsync(6, ct);
                var topRated = await catalog.TopRatedAsync(6, ct);

                return Results.Ok(new[] { mostRented, byGenre, topRated });
            })
            .WithName("GetDashboardAnalytics").WithTags("Analytics").RequireAuthorization(AppRoles.Admin);
    }
}
