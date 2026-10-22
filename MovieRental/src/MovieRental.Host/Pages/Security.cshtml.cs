namespace MovieRental.Host.Pages;

public sealed class SecurityModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("security.title", "security.lede", "security", "security");
}
