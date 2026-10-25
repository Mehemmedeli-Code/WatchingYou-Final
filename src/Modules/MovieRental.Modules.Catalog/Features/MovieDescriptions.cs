using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace MovieRental.Modules.Catalog.Features;

/// <summary>
/// Each film's longer write-up (seven to ten sentences) in English, Azerbaijani, Russian and
/// Turkish, from "seed/movie-descriptions.json" ({ slug: { en, az, ru, tr } }), shown in the
/// film's details. Kept beside the seed like the trailer list rather than in four more columns:
/// the one-line English description stays the record (and the cards' text), and a title nobody
/// has written up yet simply shows it.
/// </summary>
public static class MovieDescriptions
{
    private static Dictionary<string, Dictionary<string, string>>? cache;

    public static IReadOnlyDictionary<string, string>? For(IHostEnvironment environment, string slug)
    {
        cache ??= Load(Path.Combine(environment.ContentRootPath, "seed", "movie-descriptions.json"));
        return cache.TryGetValue(slug, out var text) ? text : null;
    }

    private static Dictionary<string, Dictionary<string, string>> Load(string file) =>
        File.Exists(file)
            ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(file)) ?? []
            : [];
}
