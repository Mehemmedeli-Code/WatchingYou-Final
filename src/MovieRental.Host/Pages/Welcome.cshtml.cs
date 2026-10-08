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
    public string ReturnUrl { get; private set; } = "/";
    public string Language => language.Code;

    public async Task<IActionResult> OnGetAsync(string? returnUrl, CancellationToken ct)
    {
        // Only a path on this site; anything else would make this page an open redirect.
        ReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith("/welcome")
            ? returnUrl : "/";

        if (currentUser.IsAuthenticated) return LocalRedirect(ReturnUrl);

        var films = await catalog.Movies.AsNoTracking()
            .OrderByDescending(m => m.PosterUrl != null && m.PosterUrl != "").ThenByDescending(m => m.AverageRating).ThenBy(m => m.Title)
            .Select(m => new Tile(m.Title, m.Genre, m.ReleaseYear, m.PosterUrl == "" ? null : m.PosterUrl))
            .Take(48)
            .ToListAsync(ct);

        // Enough tiles to cover a wide screen even with a small catalogue, shuffled per repeat
        // so the same film does not line up in a column.
        Tiles = films.Count == 0 ? [] : [.. Enumerable.Range(0, 48 / films.Count + 1)
            .SelectMany(round => films.Skip(round * 5 % films.Count).Concat(films.Take(round * 5 % films.Count)))
            .Take(48)];
        return Page();
    }
}
