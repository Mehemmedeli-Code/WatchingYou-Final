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
/// The film posters. They are stored with the site (the "posters" folder, one "{slug}.jpg" per
/// film) and handed out by our own API — the site never loads a poster from anywhere else.
/// </summary>
public static partial class MoviePosters
{
    public const string Folder = "posters";

    // Slugs are lower-case letters, digits and dashes; anything else (a "..", a slash) is refused
    // before it gets near the file system.
    [GeneratedRegex("^[a-z0-9-]{1,200}$")]
    private static partial Regex SlugPattern();

    public static string? PathFor(IHostEnvironment environment, string slug) =>
        SlugPattern().IsMatch(slug) ? Path.Combine(environment.ContentRootPath, Folder, slug + ".jpg") : null;

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/posters/{slug}", Results<PhysicalFileHttpResult, NotFound> (string slug, IHostEnvironment environment, HttpContext http) =>
            {
                var path = PathFor(environment, slug);
                if (path is null || !File.Exists(path)) return TypedResults.NotFound();
                // A poster never changes under the same name: browsers may keep it for a week.
                http.Response.Headers.CacheControl = "public, max-age=604800";
                return TypedResults.PhysicalFile(path, "image/jpeg", enableRangeProcessing: false);
            })
        .WithName("GetMoviePoster").WithTags("Catalog").AllowAnonymous();

    /// <summary>Points every film that has no poster yet at its stored one, if there is one.</summary>
    public static async Task<int> FillMissingAsync(CatalogDbContext db, IHostEnvironment environment, CancellationToken ct)
    {
        var without = await db.Movies.Where(m => m.PosterUrl == null || m.PosterUrl == "").ToListAsync(ct);
        var filled = 0;
        foreach (var movie in without)
        {
            var path = PathFor(environment, movie.Slug);
            if (path is null || !File.Exists(path)) continue;
            movie.PosterUrl = $"/api/posters/{movie.Slug}";
            filled++;
        }
        if (filled > 0) await db.SaveChangesAsync(ct);
        return filled;
    }
}
