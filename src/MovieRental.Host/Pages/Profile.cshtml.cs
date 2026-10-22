namespace MovieRental.Host.Pages;

public sealed class ProfileModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("profile.title", "profile.lede", "profile", "profile");
}
