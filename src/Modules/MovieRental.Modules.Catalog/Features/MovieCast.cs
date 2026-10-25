using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using MovieRental.SharedKernel.Results;

namespace MovieRental.Modules.Catalog.Features;

/// <summary>One actor in a film: the name, the part (when known) and the photo's address. Names and
/// Roles hold the spelling in "az", "ru" and "tr" where it differs from the English one; the client
/// picks its own language, as it does for the descriptions.</summary>
public sealed record CastMember(string Name, string? Role, string? PhotoUrl,
    IReadOnlyDictionary<string, string>? Names = null, IReadOnlyDictionary<string, string>? Roles = null);

/// <summary>
/// Who plays in each film, from "seed/movie-cast.json" ({ slug: [{ name, role, photo }] }),
/// written by tools/fetch-cast.mjs from Wikidata. The photos are in the "cast" folder and are
/// served by our own API, like the posters. A film nobody has looked up shows no cast.
/// </summary>
public static partial class MovieCast
{
    public const string Folder = "cast";
    private static Dictionary<string, List<Entry>>? cache;

    private sealed record Entry(string Name, string? Role, string? Photo,
        Dictionary<string, string>? Names = null, Dictionary<string, string>? Roles = null);

    [GeneratedRegex("^q[0-9]{1,12}\\.jpg$")]
    private static partial Regex PhotoPattern();

    public static IReadOnlyList<CastMember>? For(IHostEnvironment environment, string slug)
    {
        cache ??= Load(Path.Combine(environment.ContentRootPath, "seed", "movie-cast.json"));
        return cache.TryGetValue(slug, out var list)
            ? list.Select(a => new CastMember(a.Name, a.Role, a.Photo is null ? null : $"/api/cast/{a.Photo}", a.Names, a.Roles)).ToList()
            : null;
    }

    private static Dictionary<string, List<Entry>> Load(string file) =>
        File.Exists(file)
            ? JsonSerializer.Deserialize<Dictionary<string, List<Entry>>>(File.ReadAllText(file), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []
            : [];

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/cast/{file}", Results<PhysicalFileHttpResult, NotFound<Error>> (string file, IHostEnvironment environment, HttpContext http) =>
            {
                // Only names the fetch script writes ("q123.jpg"): no path can be asked for.
                var path = Path.Combine(environment.ContentRootPath, Folder, file);
                if (!PhotoPattern().IsMatch(file) || !File.Exists(path)) return TypedResults.NotFound(Error.NotFound("Photo"));
                http.Response.Headers.CacheControl = "public, max-age=604800";
                return TypedResults.PhysicalFile(path, "image/jpeg", enableRangeProcessing: false);
            })
        .WithName("GetCastPhoto").WithTags("Catalog").AllowAnonymous()
        .WithSummary("An actor's photo (JPEG), by the name in a film's `cast`")
        .Produces(StatusCodes.Status200OK, typeof(Stream), "image/jpeg")
        .Produces<Error>(StatusCodes.Status404NotFound);
}
