using Microsoft.AspNetCore.Authorization;

namespace MovieRental.Host.Pages;

/// <summary>SearchingYou: company for a film you have a ticket to. Signed-in only, like People.</summary>
[Authorize]
public sealed class SearchingModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("searching.title", "searching.lede", "searching", "searching");
}
