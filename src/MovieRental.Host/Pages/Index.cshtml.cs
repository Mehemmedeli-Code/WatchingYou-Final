namespace MovieRental.Host.Pages;

public sealed class IndexModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("nav.catalogue", "footer.note", "home", "home");
}
