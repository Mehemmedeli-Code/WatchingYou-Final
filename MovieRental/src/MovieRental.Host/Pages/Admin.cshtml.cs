namespace MovieRental.Host.Pages;

public sealed class AdminModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("nav.admin", "footer.note", "admin", "admin");
}
