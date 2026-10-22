using MovieRental.Host.Infrastructure;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

public sealed class AdminModel(IPageShellFactory shell) : StaffPageModel
{
    protected override string[] AllowedRoles => [AppRoles.Admin];
    public void OnGet() => View = shell.Create("nav.admin", "footer.note", "admin", "admin");
}
