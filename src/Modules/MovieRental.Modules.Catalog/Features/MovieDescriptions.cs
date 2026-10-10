using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace MovieRental.Modules.Catalog.Features;

/// <summary>
/// The catalogue's descriptions in Azerbaijani, Russian and Turkish, from
/// "seed/movie-descriptions.json" ({ slug: { az, ru, tr } }). Kept beside the seed like the
/// trailer list rather than in three more columns: the English text stays the record, and a
/// title nobody has translated yet simply shows it.
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
