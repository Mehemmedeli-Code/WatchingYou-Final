namespace MovieRental.Host.Pages;

public sealed class HumanCraftModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("gallery.human.title", "gallery.human.lede", "human-craft", "humanCraft");
}
