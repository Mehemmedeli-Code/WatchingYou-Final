using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MovieRental.Host.Infrastructure.Localization;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

/// <summary>
/// The front door for anyone who is not signed in: a wall of the catalogue's own posters, one
/// line on what this is, an e-mail box that leads to sign-up, and the questions people ask
/// first. Signed-in visitors never see it — they go straight on to the site.
/// </summary>
public sealed class WelcomeModel(CatalogDbContext catalog, ICurrentUser currentUser, ILanguageContext language) : PageModel
{
    /// <summary>One tile per film: its poster when it has one, otherwise its title and genre
    /// set as a poster of our own — the wall is never empty.</summary>
    public sealed record Tile(string Title, string Genre, int Year, string? PosterUrl);

    public IReadOnlyList<Tile> Tiles { get; private set; } = [];

    /// <summary>Below the fold: the best of a few genres, numbered, as a reason to sign up.</summary>
    public sealed record RankedFilm(string Title, int Year, string PosterUrl);
    public sealed record TopRow(string Genre, IReadOnlyList<RankedFilm> Films);
    public IReadOnlyList<TopRow> TopRows { get; private set; } = [];
    private static readonly string[] RankedGenres = ["Drama", "Horror", "Science Fiction"];
    public string ReturnUrl { get; private set; } = "/";
    public string Language => language.Code;

    public async Task<IActionResult> OnGetAsync(string? returnUrl, CancellationToken ct)
    {
        // Only a path on this site; anything else would make this page an open redirect.
        ReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith("/welcome")
            ? returnUrl : "/";

        if (currentUser.IsAuthenticated) return LocalRedirect(ReturnUrl);

        // Only films that can be played: no Originals, nothing without a video or trailer.
        var watchable = catalog.Movies.AsNoTracking().Where(m => !m.IsOriginal && ((m.VideoUrl != null && m.VideoUrl != "") || (m.TrailerUrl != null && m.TrailerUrl != "")));
        var films = await watchable
            .OrderByDescending(m => m.PosterUrl != null && m.PosterUrl != "").ThenByDescending(m => m.AverageRating).ThenBy(m => m.Title)
            .Select(m => new Tile(m.Title, m.Genre, m.ReleaseYear, m.PosterUrl == "" ? null : m.PosterUrl))
            .Take(48)
            .ToListAsync(ct);

        // Rated first, then the most reviewed, then the newest: a ranking that still reads well
        // while few people have rated anything. Only films with a poster: a row of numbers needs faces.
        var ranked = await watchable
            .Where(m => RankedGenres.Contains(m.Genre) && m.PosterUrl != null && m.PosterUrl != "")
            .OrderByDescending(m => m.AverageRating).ThenByDescending(m => m.ReviewCount).ThenByDescending(m => m.ReleaseYear)
            .Select(m => new { m.Genre, Film = new RankedFilm(m.Title, m.ReleaseYear, m.PosterUrl!) })
            .ToListAsync(ct);
        TopRows = [.. RankedGenres
            .Select(g => new TopRow(g, [.. ranked.Where(r => r.Genre == g).Select(r => r.Film).Take(10)]))
            .Where(row => row.Films.Count >= 3)];

        // Enough tiles to cover a wide screen even with a small catalogue, shuffled per repeat
        // so the same film does not line up in a column.
        Tiles = films.Count == 0 ? [] : [.. Enumerable.Range(0, 48 / films.Count + 1)
            .SelectMany(round => films.Skip(round * 5 % films.Count).Concat(films.Take(round * 5 % films.Count)))
            .Take(48)];
        return Page();
    }
}
