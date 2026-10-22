using MovieRental.Host.Infrastructure;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

public sealed class SecurityModel(IPageShellFactory shell) : StaffPageModel
{
    // Moderators work here too, on the help requests and reported messages only.
    protected override string[] AllowedRoles => [AppRoles.Security, AppRoles.Admin, AppRoles.Moderator];
    public void OnGet() => View = shell.Create("security.title", "security.lede", "security", "security");
}
