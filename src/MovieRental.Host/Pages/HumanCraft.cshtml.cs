using Microsoft.AspNetCore.Authorization;
namespace MovieRental.Host.Pages;

[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class HumanCraftModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("gallery.human.title", "gallery.human.lede", "human-craft", "humanCraft");
}
