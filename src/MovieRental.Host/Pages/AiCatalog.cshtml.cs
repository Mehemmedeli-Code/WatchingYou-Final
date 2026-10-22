using Microsoft.AspNetCore.Authorization;
namespace MovieRental.Host.Pages;

[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class AiCatalogModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("gallery.ai.title", "gallery.ai.lede", "ai-catalog", "aiCatalog");
}
