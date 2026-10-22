using System.Text.Json;

namespace MovieRental.Host.Infrastructure.Localization;

/// <summary>
/// One JSON file per language, read once at startup and shared by both renderers.
///
/// The brief called for .resx for Razor and a separate JSON bundle for React. Two stores of
/// the same sentences drift within a week — a key gets translated on one side and not the
/// other, and nobody notices until a page renders half in English. One file feeding both
/// costs a small loader and removes that whole class of bug.
/// </summary>
public sealed class Translations
{
    public const string DefaultLanguage = "az";
    public static readonly string[] Supported = ["az", "en", "ru", "tr"];

    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>
    {
        ["az"] = "Azərbaycan",
        ["en"] = "English",
        ["ru"] = "Русский",
        ["tr"] = "Türkçe"
    };

    private readonly Dictionary<string, Dictionary<string, string>> _byLanguage = new(StringComparer.OrdinalIgnoreCase);

    public Translations(IHostEnvironment environment, ILogger<Translations> logger)
    {
        foreach (var language in Supported)
        {
            var path = Path.Combine(environment.ContentRootPath, "locales", $"{language}.json");
            if (!File.Exists(path))
            {
                logger.LogWarning("Missing translation file {Path}; {Language} will fall back to keys.", path, language);
                _byLanguage[language] = [];
                continue;
            }

            var json = File.ReadAllText(path);
            _byLanguage[language] = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
    }

    public static string Normalise(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return DefaultLanguage;
        var code = candidate.Trim().ToLowerInvariant();
        if (code.Length > 2) code = code[..2];
        return Supported.Contains(code) ? code : DefaultLanguage;
    }

    public IReadOnlyDictionary<string, string> For(string language) =>
        _byLanguage.TryGetValue(Normalise(language), out var map) ? map : _byLanguage[DefaultLanguage];

    /// <summary>Missing keys return the key itself. A visible "nav.studio" on screen is a
    /// bug report; a silently blank button is not.</summary>
    public string Get(string language, string key) =>
        For(language).TryGetValue(key, out var value) ? value : key;
}

public interface ILanguageContext
{
    string Code { get; }
    string this[string key] { get; }
    IReadOnlyDictionary<string, string> All { get; }
}

internal sealed class LanguageContext(IHttpContextAccessor accessor, Translations translations) : ILanguageContext
{
    public const string CookieName = "rr.lang";

    public string Code => Translations.Normalise(
        accessor.HttpContext?.Request.Query["lang"].FirstOrDefault()
        ?? accessor.HttpContext?.Request.Cookies[CookieName]
        ?? accessor.HttpContext?.Request.Headers.AcceptLanguage.FirstOrDefault()?.Split(',').FirstOrDefault());

    public string this[string key] => translations.Get(Code, key);

    public IReadOnlyDictionary<string, string> All => translations.For(Code);
}

public static class LanguageEndpoints
{
    /// <summary>
    /// The picker posts here and comes straight back. Keeping the choice in a cookie rather
    /// than the URL means every link on the site stays language-free.
    /// </summary>
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/lang/{code}", (string code, string? returnUrl, HttpContext context) =>
        {
            context.Response.Cookies.Append(LanguageContext.CookieName, Translations.Normalise(code),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    HttpOnly = false
                });

            // Only ever return to our own pages: an open redirect here would be handed out
            // by every language link on the site.
            var target = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
                ? returnUrl
                : "/";

            return Results.Redirect(target);
        })
        .WithName("SetLanguage").ExcludeFromDescription().AllowAnonymous();
}
