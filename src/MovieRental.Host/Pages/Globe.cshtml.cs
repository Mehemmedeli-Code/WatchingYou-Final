using Microsoft.AspNetCore.Authorization;
namespace MovieRental.Host.Pages;

[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class GlobeModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("globe.title", "globe.lede", "globe", "globe");
}
