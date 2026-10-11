namespace MovieRental.Host.Pages;

/// <summary>Watch-order guides: series and directors in the order worth watching them. Open to
/// anyone — reading a guide needs no account.</summary>
public sealed class GuideModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("guide.title", "guide.lede", "guide", "guide");
}
