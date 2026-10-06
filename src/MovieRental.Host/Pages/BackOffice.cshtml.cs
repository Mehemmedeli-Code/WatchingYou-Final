using Microsoft.AspNetCore.Authorization;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

/// <summary>
/// The back office: the cinema's own management system, apart from the public site. It has
/// its own layout — a sidebar and no public nav — because the people using it are at a till
/// or behind a desk, not browsing films.
///
/// Guarded on the page as well as on every API it calls. An anonymous visitor is sent to
/// sign in and comes straight back here.
/// </summary>
[Authorize(Policy = AppPolicies.BackOffice)]
public sealed class BackOfficeModel(IPageShellFactory shell) : AppPageModel
{
    public void OnGet() => View = shell.Create("nav.backoffice", "footer.note", "backoffice", "backoffice");
}
