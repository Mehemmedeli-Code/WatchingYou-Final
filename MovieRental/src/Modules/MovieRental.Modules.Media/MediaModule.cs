using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRental.Modules.Media.Features;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Modules;

namespace MovieRental.Modules.Media;

public sealed class MediaModule : IModule
{
    public string Name => "Media";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddDbContext<MediaDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrations", MediaDbContext.SchemaName)));

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        UploadShortFilmEndpoints.Map(endpoints);
        StudioEndpoints.Map(endpoints);
        StreamShortFilmEndpoint.Map(endpoints);
        GalleryEndpoints.Map(endpoints);
        SecurityReviewEndpoints.Map(endpoints);
        ReviewShortFilmEndpoints.Map(endpoints);
    }
}
