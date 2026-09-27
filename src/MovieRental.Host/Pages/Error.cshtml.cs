using Microsoft.AspNetCore.Mvc;
using MovieRental.Host.Infrastructure.Localization;

namespace MovieRental.Host.Pages;

/// <summary>
/// Rendered entirely by Razor, with no React island.
///
/// That is deliberate: the times you most need an error page are the times the bundle failed
/// to load. A page that depends on the same JavaScript that just broke is not an error page.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ErrorModel(IPageShellFactory shell, ILanguageContext language) : AppPageModel
{
    public int StatusCode { get; private set; } = 500;

    /// <summary>Exposed by the model rather than reached for in the markup: a view should be
    /// handed what it renders, not go looking for it.</summary>
    public string TraceId { get; private set; } = "";
    public string Heading { get; private set; } = "";
    public string Explanation { get; private set; } = "";

    public void OnGet(int? code)
    {
        StatusCode = code is >= 400 and < 600 ? code.Value : 500;

        // Only the codes a visitor can actually provoke are given their own words. Anything
        // else falls back to the generic message rather than inventing an explanation.
        var key = StatusCode switch
        {
            404 => "error.notFound",
            403 => "error.forbidden",
            401 => "error.unauthorised",
            429 => "error.tooMany",
            _ => "error.server"
        };

        Heading = language[$"{key}.title"];
        Explanation = language[$"{key}.body"];

        // Keep the real status on the response. A 404 rendered with a 200 header tells every
        // crawler and monitor that the page was fine.
        Response.StatusCode = StatusCode;
        TraceId = HttpContext.TraceIdentifier;

        View = shell.Create($"{key}.title", $"{key}.body", "", "error");
    }
}
