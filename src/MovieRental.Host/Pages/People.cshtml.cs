namespace MovieRental.Host.Pages;

public sealed class PeopleModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("people.title", "people.lede", "people", "people");
}
