using Microsoft.AspNetCore.Authorization;
namespace MovieRental.Host.Pages;

[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class HelpModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("help.title", "help.lede", "help", "help");
}
