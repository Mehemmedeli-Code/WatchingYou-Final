namespace MovieRental.Host.Pages;

public sealed class StudioModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("studio.title", "studio.lede", "studio", "studio");
}
