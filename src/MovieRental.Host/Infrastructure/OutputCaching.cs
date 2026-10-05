using Microsoft.AspNetCore.OutputCaching;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// Server-side caching for the public read endpoints — the catalogue, the schedule, the
/// cinemas. Every visitor to the home page asks for the same first page of films; answering
/// that from memory for a minute takes the database out of the busiest request on the site.
///
/// Why a custom policy rather than the built-in default: the default refuses to cache any
/// request that carries an Authorization header, and a signed-in visitor's browser always
/// sends one. These responses are identical for everybody, so who is asking does not matter.
/// Anything personal (watchlist, recommendations, tickets) is simply not marked cacheable.
///
/// Staleness is handled two ways: short lifetimes, and eviction by tag the moment a write
/// succeeds on a path that could change what is cached (see <see cref="EvictOnWrite"/>).
/// </summary>
public sealed class PublicApiCachePolicy(string tag, TimeSpan lifetime) : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var request = context.HttpContext.Request;
        var cacheable = (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            // The admin's restore screen sees withdrawn titles; that answer must never be
            // served to anyone else from the cache.
            && !string.Equals(request.Query["includeDeleted"], "true", StringComparison.OrdinalIgnoreCase);

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = cacheable;
        context.AllowCacheStorage = cacheable;
        context.AllowLocking = true;
        context.CacheVaryByRules.QueryKeys = "*";
        // The catalogue answers in one language whatever the page, but vary on it anyway in
        // case a translated field is added later.
        context.CacheVaryByRules.HeaderNames = "Accept-Language";
        context.ResponseExpirationTimeSpan = lifetime;
        context.Tags.Add(tag);
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var response = context.HttpContext.Response;
        // Only clean successes are kept: an error or anything that sets a cookie is personal.
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.SetCookie.Count > 0)
            context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }
}

public static class OutputCaching
{
    public static IServiceCollection AddPublicApiCaching(this IServiceCollection services) =>
        services.AddOutputCache(options =>
        {
            options.AddPolicy(AppPolicies.CatalogueCache, new PublicApiCachePolicy(CacheTags.Catalogue, TimeSpan.FromSeconds(60)));
            options.AddPolicy(AppPolicies.CinemaCache, new PublicApiCachePolicy(CacheTags.Cinema, TimeSpan.FromSeconds(15)));
            options.AddPolicy(AppPolicies.VenueCache, new PublicApiCachePolicy(CacheTags.Cinema, TimeSpan.FromMinutes(5)));
        });

    /// <summary>
    /// After a successful write, drops the cached answers it could have changed. Done here, by
    /// path, rather than in each handler: the modules stay unaware of the cache, and a new
    /// write endpoint under one of these paths is covered without anyone remembering to.
    /// </summary>
    public static IApplicationBuilder EvictOnWrite(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next();

            var request = context.Request;
            if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)) return;
            if (context.Response.StatusCode >= 400) return;

            var path = request.Path;
            var store = context.RequestServices.GetRequiredService<IOutputCacheStore>();

            // Renting changes a film's copies on the shelf; reviewing changes its rating.
            if (path.StartsWithSegments("/api/movies") || path.StartsWithSegments("/api/admin/movies") ||
                path.StartsWithSegments("/api/rentals") || path.StartsWithSegments("/api/admin/rentals"))
                await store.EvictByTagAsync(CacheTags.Catalogue, default);

            // Every seat sold or released changes "seats left" on the schedule.
            if (path.StartsWithSegments("/api/screenings") || path.StartsWithSegments("/api/admin/screenings") ||
                path.StartsWithSegments("/api/bookings") || path.StartsWithSegments("/api/admin/venues"))
                await store.EvictByTagAsync(CacheTags.Cinema, default);
        });
}
