namespace MovieRental.Host.Pages;

public sealed class SupportModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("support.title", "support.lede", "support", "support");
}
