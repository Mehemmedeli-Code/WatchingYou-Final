namespace MovieRental.Host.Pages;

public sealed class OnDisplayModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("onDisplay.title", "onDisplay.lede", "on-display", "onDisplay");
}
