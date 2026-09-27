using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRental.Modules.Cinema.Features;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Infrastructure;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Modules;

namespace MovieRental.Modules.Cinema;

public sealed class CinemaModule : IModule
{
    public string Name => "Cinema";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CinemaDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrations", CinemaDbContext.SchemaName)));

        // Sweeps up checkouts that were paid for but never confirmed, so their seats go
        // back on sale instead of sitting blocked.
        // A pure rule with no state; one instance serves every request.
        services.AddSingleton<RefundPolicy>();

        services.AddHostedService<HoldExpiryService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        SeatMapEndpoints.Map(endpoints);
        BookSeatsEndpoint.Map(endpoints);
        MoviesOnDisplayEndpoint.Map(endpoints);
        VenueEndpoints.Map(endpoints);
        CheckInEndpoint.Map(endpoints);
        RefundEndpoints.Map(endpoints);
        ManageScreeningsEndpoints.Map(endpoints);
    }
}
