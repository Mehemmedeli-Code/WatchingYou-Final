namespace MovieRental.Host.Pages;

public sealed class CinemaModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("nav.cinema", "footer.note", "cinema", "cinema");
}
