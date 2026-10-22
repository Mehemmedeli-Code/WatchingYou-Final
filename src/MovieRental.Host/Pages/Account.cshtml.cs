using Microsoft.AspNetCore.Authorization;
namespace MovieRental.Host.Pages;

[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class AccountModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("account.signIn", "footer.note", "account", "account");
}
