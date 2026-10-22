using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRental.Modules.Rentals.Domain;
using MovieRental.Modules.Rentals.Features;
using MovieRental.Modules.Rentals.Infrastructure;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Modules;

namespace MovieRental.Modules.Rentals;

public sealed class RentalsModule : IModule
{
    public string Name => "Rentals";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RentalsDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrations", RentalsDbContext.SchemaName)));

        services.AddSingleton(configuration.GetSection("Rentals:LateFee").Get<LateFeePolicy>() ?? new LateFeePolicy());
        services.AddScoped<IRentalAnalytics, RentalAnalytics>();
        services.AddHostedService<DueDateNotificationService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RentMovieEndpoint.Map(endpoints);
        ReturnAndExtendEndpoints.Map(endpoints);
        RentalHistoryEndpoints.Map(endpoints);
    }
}
