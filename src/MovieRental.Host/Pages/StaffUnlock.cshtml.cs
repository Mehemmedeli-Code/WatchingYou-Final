using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MovieRental.Host.Infrastructure;
using MovieRental.Host.Infrastructure.Localization;
using MovieRental.Modules.Identity.Features;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

/// <summary>
/// The staff PIN, asked on a page of its own — away from the site, with nothing on it but the
/// question. Reached only by signed-in staff; to anyone else it does not exist.
/// </summary>
// No per-address limit: it would share the sign-in budget with everyone behind the same office
// address. Guessing is stopped per account instead (StaffPin: 5 tries, then 15 minutes).
[AllowAnonymous]   // the page itself decides who may see what (see AppPageModel)
public sealed class StaffUnlockModel(IDispatcher dispatcher, ILanguageContext language) : PageModel
{
    public string? Problem { get; private set; }
    public string Language => language.Code;
    public string ReturnUrl { get; private set; } = "";

    private bool IsStaff => User.Identity?.IsAuthenticated == true && AppRoles.Staff.Any(User.IsInRole);

    public IActionResult OnGet(string? returnUrl)
    {
        if (!IsStaff) return NotFound();
        ReturnUrl = Safe(returnUrl);
        return StaffUnlock.IsUnlocked(HttpContext) ? LocalRedirect(ReturnUrl) : Page();
    }

    public async Task<IActionResult> OnPostAsync(string? pin, string? returnUrl, CancellationToken ct)
    {
        if (!IsStaff) return NotFound();
        ReturnUrl = Safe(returnUrl);

        var result = await dispatcher.Send(new UnlockStaffCommand(pin ?? ""), ct);
        switch (result.Outcome)
        {
            case StaffUnlockOutcome.Unlocked:
                StaffUnlock.Issue(HttpContext);
                return LocalRedirect(ReturnUrl);
            case StaffUnlockOutcome.NotStaff:
                return NotFound();
            case StaffUnlockOutcome.NoPin:
                Problem = language["staff.noPin"];
                break;
            case StaffUnlockOutcome.Locked:
                var minutes = Math.Max(1, (int)Math.Ceiling(((result.LockedUntilUtc ?? DateTime.UtcNow) - DateTime.UtcNow).TotalMinutes));
                Problem = string.Format(language["staff.locked"], minutes);
                break;
            default:
                Problem = string.Format(language["staff.wrong"], result.AttemptsLeft);
                break;
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    /// <summary>Back to the staff screen that sent them here, never anywhere else; with nothing
    /// to go back to, the screen their role works in.</summary>
    private string Safe(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            && !returnUrl.StartsWith(StaffPaths.Unlock, StringComparison.Ordinal)
            && (returnUrl.StartsWith(StaffPaths.Base + "/", StringComparison.Ordinal)
                || (returnUrl == "/swagger/index.html" && User.IsInRole(AppRoles.Admin))))
            return returnUrl;
        if (User.IsInRole(AppRoles.Admin)) return StaffPaths.Admin;
        if (User.IsInRole(AppRoles.Cashier)) return StaffPaths.BackOffice;
        return StaffPaths.Security;
    }
}
