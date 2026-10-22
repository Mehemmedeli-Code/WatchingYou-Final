using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MovieRental.Host.Infrastructure.Localization;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Pages;

/// <summary>
/// Every page carries a strongly-typed view model. ViewBag and ViewData are deliberately
/// unused across this project: they are untyped, invisible to the compiler and silently
/// return null when a key is misspelled.
/// </summary>
public abstract class AppPageModel : PageModel
{
    public AppPageViewModel View { get; protected set; } = AppPageViewModel.Empty;
}

public sealed record NavItem(string Key, string Href, string Label)
{
    public bool IsCurrent { get; init; }
}

public sealed record LanguageOption(string Code, string Name, bool IsCurrent);

public sealed record AppPageViewModel(
    string Title,
    string Tagline,
    string ActiveNav,
    string ReactMount,
    string Language,
    IReadOnlyList<NavItem> Nav,
    IReadOnlyList<LanguageOption> Languages,
    bool IsSignedIn,
    bool IsAdmin,
    string? UserName,
    string TranslationsJson,
    FrontendAssets Assets)
{
    public static readonly AppPageViewModel Empty = new(
        "Reel & Row", string.Empty, string.Empty, "home", Translations.DefaultLanguage,
        [], [], false, false, null, "{}", new FrontendAssets(false, string.Empty));
}

/// <summary>
/// Builds the shell once, in one place. Nav visibility is decided here from the signed-in
/// roles, which is what makes "absent from the DOM" achievable — a page model cannot forget
/// to hide something it never receives.
/// </summary>
public interface IPageShellFactory
{
    AppPageViewModel Create(string titleKey, string taglineKey, string activeNav, string reactMount);
}

internal sealed class PageShellFactory(
    IConfiguration configuration, ILanguageContext language, ICurrentUser currentUser) : IPageShellFactory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AppPageViewModel Create(string titleKey, string taglineKey, string activeNav, string reactMount)
    {
        var isSignedIn = currentUser.IsAuthenticated;
        var isAdmin = currentUser.IsInRole(AppRoles.Admin);
        var isSecurity = isAdmin || currentUser.IsInRole(AppRoles.Security);

        var nav = new List<NavItem>
        {
            new("home", "/", language["nav.catalogue"]),
            new("on-display", "/on-display", language["nav.onDisplay"]),
            new("ai-catalog", "/ai-catalog", language["nav.aiCatalog"]),
            new("human-craft", "/human-craft", language["nav.humanCraft"]),
            new("cinema", "/cinema", language["nav.cinema"])
        };

        if (isSignedIn)
        {
            nav.Add(new NavItem("rentals", "/rentals", language["nav.rentals"]));
            nav.Add(new NavItem("studio", "/studio", language["nav.studio"]));
        }

        if (isSecurity) nav.Add(new NavItem("security", "/security", language["nav.security"]));
        if (isAdmin)
        {
            nav.Add(new NavItem("admin", "/admin", language["nav.admin"]));
            nav.Add(new NavItem("api", "/swagger", language["nav.api"]));
        }

        var languages = Translations.Supported
            .Select(code => new LanguageOption(code, Translations.DisplayNames[code], code == language.Code))
            .ToArray();

        return new AppPageViewModel(
            Title: language[titleKey],
            Tagline: language[taglineKey],
            ActiveNav: activeNav,
            ReactMount: reactMount,
            Language: language.Code,
            Nav: [.. nav.Select(item => item with { IsCurrent = item.Key == activeNav })],
            Languages: languages,
            IsSignedIn: isSignedIn,
            IsAdmin: isAdmin,
            UserName: isSignedIn ? currentUser.Email : null,
            // Inlined rather than fetched: a separate request would show untranslated text
            // for the moment it takes to arrive.
            TranslationsJson: JsonSerializer.Serialize(language.All, Json),
            Assets: FrontendAssets.From(configuration));
    }
}

/// <summary>
/// Resolves where the React bundle comes from. In development the Vite dev server serves
/// modules straight from disk with hot reload; in any other environment the built bundle
/// sits in wwwroot/app with fixed filenames, so no manifest lookup is needed.
/// </summary>
public sealed record FrontendAssets(bool UseDevServer, string DevServerUrl)
{
    public string ScriptUrl => UseDevServer ? $"{DevServerUrl}/src/main.tsx" : "/app/app.js";
    public string? StyleUrl => UseDevServer ? null : "/app/app.css";
    public string? ViteClientUrl => UseDevServer ? $"{DevServerUrl}/@vite/client" : null;

    public static FrontendAssets From(IConfiguration configuration) => new(
        configuration.GetValue("Frontend:UseDevServer", false),
        configuration["Frontend:DevServerUrl"] ?? "http://localhost:5173");
}
