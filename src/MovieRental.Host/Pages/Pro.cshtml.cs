namespace MovieRental.Host.Pages;

/// <summary>Watching PRO: what it costs, what it gives, and the form to join. Open to anyone —
/// people decide before they sign up — while paying needs an account.</summary>
public sealed class ProModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("pro.title", "footer.note", "pro", "pro");
}
