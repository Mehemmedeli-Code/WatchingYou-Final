using Microsoft.AspNetCore.Authorization;
namespace MovieRental.Host.Pages;

/// <summary>Someone's profile by @handle. The same island as /profile; it reads the handle from the URL.</summary>
[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class UserProfileModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("nav.profile", "profile.lede", "people", "profile");
}
