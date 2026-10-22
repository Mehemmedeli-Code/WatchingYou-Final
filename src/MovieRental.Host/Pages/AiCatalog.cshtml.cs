namespace MovieRental.Host.Pages;

public sealed class AiCatalogModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("gallery.ai.title", "gallery.ai.lede", "ai-catalog", "aiCatalog");
}
