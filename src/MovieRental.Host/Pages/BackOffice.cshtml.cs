using Microsoft.AspNetCore.Authorization;
using MovieRental.Host.Infrastructure;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

/// <summary>
/// The back office: the cinema's own management system, apart from the public site. It has
/// its own layout — a sidebar and no public nav — because the people using it are at a till
/// or behind a desk, not browsing films.
///
/// Guarded on the page as well as on every API it calls.
/// </summary>
[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class BackOfficeModel(IPageShellFactory shell) : StaffPageModel
{
    protected override string[] AllowedRoles => [AppRoles.Cashier, AppRoles.Admin];
    public void OnGet() => View = shell.Create("nav.backoffice", "footer.note", "backoffice", "backoffice");
}
