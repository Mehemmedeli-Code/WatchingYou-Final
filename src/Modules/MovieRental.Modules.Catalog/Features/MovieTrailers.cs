using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Catalog.Persistence;

namespace MovieRental.Modules.Catalog.Features;

/// <summary>
/// The film trailers, which the Watch button plays. Two kinds, both handed out by our own API
/// as the film's TrailerUrl:
///  - a freely licensed trailer stored with the site ("trailers/{slug}.webm", mostly classics
///    whose trailers are in the public domain), streamed by /api/trailers/{slug};
///  - for the rest, the official trailer on YouTube ("trailers/trailers.json" maps a film's
///    slug to it), which plays in YouTube's embedded player — a studio's trailer may not be
///    copied onto another site, only embedded.
/// </summary>
public static partial class MovieTrailers
{
    public const string Folder = "trailers";

    [GeneratedRegex("^[a-z0-9-]{1,200}$")]
    private static partial Regex SlugPattern();

    public static string? PathFor(IHostEnvironment environment, string slug) =>
        SlugPattern().IsMatch(slug) ? Path.Combine(environment.ContentRootPath, Folder, slug + ".webm") : null;

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/trailers/{slug}", Results<PhysicalFileHttpResult, NotFound> (string slug, IHostEnvironment environment, HttpContext http) =>
            {
                var path = PathFor(environment, slug);
                if (path is null || !File.Exists(path)) return TypedResults.NotFound();
                http.Response.Headers.CacheControl = "public, max-age=604800";
                // Range requests, so the player can seek and start before the whole file arrives.
                return TypedResults.PhysicalFile(path, "video/webm", enableRangeProcessing: true);
            })
        .WithName("GetMovieTrailer").WithTags("Catalog").AllowAnonymous();

    /// <summary>Gives every film without a trailer its stored one, or else its YouTube one.</summary>
    public static async Task<int> FillMissingAsync(CatalogDbContext db, IHostEnvironment environment, CancellationToken ct)
    {
        var listFile = Path.Combine(environment.ContentRootPath, Folder, "trailers.json");
        var youTube = File.Exists(listFile)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(listFile, ct)) ?? []
            : [];

        var without = await db.Movies.Where(m => m.TrailerUrl == null || m.TrailerUrl == "").ToListAsync(ct);
        var filled = 0;
        foreach (var movie in without)
        {
            var path = PathFor(environment, movie.Slug);
            if (path is not null && File.Exists(path)) movie.TrailerUrl = $"/api/trailers/{movie.Slug}";
            else if (youTube.TryGetValue(movie.Slug, out var videoId) && Regex.IsMatch(videoId, "^[A-Za-z0-9_-]{11}$"))
                movie.TrailerUrl = $"https://www.youtube.com/watch?v={videoId}";
            else continue;
            filled++;
        }
        if (filled > 0) await db.SaveChangesAsync(ct);
        return filled;
    }
}
