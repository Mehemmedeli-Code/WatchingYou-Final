namespace MovieRental.Host.Pages;

public sealed class RentalsModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("nav.rentals", "footer.note", "rentals", "rentals");
}
