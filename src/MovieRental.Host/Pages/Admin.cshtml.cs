using Microsoft.AspNetCore.Authorization;
using MovieRental.Host.Infrastructure;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class AdminModel(IPageShellFactory shell) : StaffPageModel
{
    protected override string[] AllowedRoles => [AppRoles.Admin];
    public void OnGet() => View = shell.Create("nav.admin", "footer.note", "admin", "admin");
}
