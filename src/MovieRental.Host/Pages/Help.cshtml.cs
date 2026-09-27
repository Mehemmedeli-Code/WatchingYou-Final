namespace MovieRental.Host.Pages;

public sealed class HelpModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("help.title", "help.lede", "help", "help");
}
