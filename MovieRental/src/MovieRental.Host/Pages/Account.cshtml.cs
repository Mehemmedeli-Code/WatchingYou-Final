namespace MovieRental.Host.Pages;

public sealed class AccountModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("account.signIn", "footer.note", "account", "account");
}
