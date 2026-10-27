using Microsoft.AspNetCore.Authorization;
namespace MovieRental.Host.Pages;

/// <summary>The daily cinema paper: film news from the trade press, no politics. Open to anyone,
/// like the guides.</summary>
[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class NewsModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("news.title", "news.lede", "news", "news");
}
