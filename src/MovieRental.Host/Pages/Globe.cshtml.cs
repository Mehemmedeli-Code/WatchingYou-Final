namespace MovieRental.Host.Pages;

public sealed class GlobeModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("globe.title", "globe.lede", "globe", "globe");
}
