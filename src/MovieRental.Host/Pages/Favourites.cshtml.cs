using Microsoft.AspNetCore.Authorization;

namespace MovieRental.Host.Pages;

/// <summary>The watchlist on its own page. A list of saved films belongs to an account, so an
/// anonymous visitor is sent to sign in and brought straight back.</summary>
[Authorize]
public sealed class FavouritesModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("nav.favourites", "footer.note", "favourites", "favourites");
}
