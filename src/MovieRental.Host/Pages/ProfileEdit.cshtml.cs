namespace MovieRental.Host.Pages;

public sealed class ProfileEditModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("profile.edit", "profile.lede", "profile", "profileEdit");
}
