using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRental.Modules.Catalog.Features;
using MovieRental.Modules.Catalog.Infrastructure;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Modules;

namespace MovieRental.Modules.Catalog;

public sealed class CatalogModule : IModule
{
    public string Name => "Catalog";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrations", CatalogDbContext.SchemaName)));

        services.AddScoped<ICatalogApi, CatalogApi>();
        services.AddScoped<ICatalogAnalytics, CatalogAnalytics>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        GetMoviesEndpoints.Map(endpoints);
        GetMovieByIdEndpoint.Map(endpoints);
        ManageMoviesEndpoints.Map(endpoints);
        AddReviewEndpoint.Map(endpoints);
        SeedMoviesEndpoint.Map(endpoints);
    }
}
