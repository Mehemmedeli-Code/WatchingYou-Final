using Microsoft.AspNetCore.Mvc;
using MovieRental.Host.Infrastructure.Localization;

namespace MovieRental.Host.Pages;

/// <summary>
/// The pages behind the footer links: about, blog, careers, the API and the terms. Plain
/// server-rendered text like the privacy policy, so they read without JavaScript and search
/// engines see them. Each is written per language; the facts in them (prices, refund rules,
/// rate limits) are taken from the code and have to change with it.
/// </summary>
public sealed class InfoModel(IPageShellFactory shell, ILanguageContext language, IConfiguration configuration) : AppPageModel
{
    public InfoPage Text { get; private set; } = InfoPages.For("about", "en");
    public string Slug { get; private set; } = "about";
    public string ContactEmail { get; private set; } = "";

    public IActionResult OnGet(string slug)
    {
        if (!InfoPages.Slugs.Contains(slug)) return NotFound();
        Slug = slug;
        Text = InfoPages.For(slug, language.Code);
        ContactEmail = configuration["Legal:ContactEmail"] ?? "";
        View = shell.Create($"footer.{slug}", "footer.note", slug, "info");
        return Page();
    }
}

public sealed record InfoSection(string Heading, IReadOnlyList<string> Paragraphs, IReadOnlyList<string>? Code = null, string? Meta = null);

public sealed record InfoPage(string Title, string Lede, IReadOnlyList<InfoSection> Sections);

public static partial class InfoPages
{
    public static readonly string[] Slugs = ["about", "blog", "careers", "developers", "terms", "mobile-app", "non-users"];

    // A switch rather than a static dictionary: the pages live in other files of this partial
    // class, and static fields across partial files initialise in no guaranteed order — a
    // dictionary built here could capture them while they are still null.
    public static InfoPage For(string slug, string language) => (slug, language) switch
    {
        ("about", "az") => AboutAz, ("about", "ru") => AboutRu, ("about", "tr") => AboutTr, ("about", _) => AboutEn,
        ("blog", "az") => BlogAz, ("blog", "ru") => BlogRu, ("blog", "tr") => BlogTr, ("blog", _) => BlogEn,
        ("careers", "az") => CareersAz, ("careers", "ru") => CareersRu, ("careers", "tr") => CareersTr, ("careers", _) => CareersEn,
        ("developers", "az") => DevelopersAz, ("developers", "ru") => DevelopersRu, ("developers", "tr") => DevelopersTr, ("developers", _) => DevelopersEn,
        ("mobile-app", "az") => AppAz, ("mobile-app", "ru") => AppRu, ("mobile-app", "tr") => AppTr, ("mobile-app", _) => AppEn,
        ("non-users", "az") => NonUsersAz, ("non-users", "ru") => NonUsersRu, ("non-users", "tr") => NonUsersTr, ("non-users", _) => NonUsersEn,
        ("terms", "az") => TermsAz, ("terms", "ru") => TermsRu, ("terms", "tr") => TermsTr, _ => TermsEn,
    };

    // The endpoint table is the same in every language.
    private static string[] PublicEndpoints =>
    [
        "GET    /api/movies                                catalogue, 12 per page (pageSize up to 60)",
        "         ?search= &genre= &sortBy= &page= &pageSize=",
        "GET    /api/movies/{id}                           one film",
        "GET    /api/movies/genres                         genre list",
        "GET    /api/screenings                            upcoming cinema shows",
        "GET    /api/screenings/{id}/seats                 seat map with taken seats",
        "GET    /api/gallery/ai                            published AI short films",
        "GET    /api/gallery/human                         published handmade short films",
        "GET    /api/health                                service health",
    ];

    private static string[] MemberEndpoints =>
    [
        "POST   /api/auth/register      { fullName, email, password, phoneNumber? }",
        "POST   /api/auth/login         { email, password }  ->  { accessToken, refreshToken, user }",
        "POST   /api/auth/refresh       { refreshToken }     ->  a new pair (the old one stops working)",
        "GET    /api/auth/me            your profile",
        "PUT    /api/auth/profile       { fullName, phoneNumber?, username?, bio?, isPrivate? }",
        "GET    /api/people?q=          search members by @username or name",
        "GET    /api/people/u/{username} a profile",
        "POST   /api/people/{id}/follow follow (or request, for a private account)",
        "GET    /api/shorts/by/{userId}?tab=public|private&origin=ai|human   a profile's films",
        "POST   /api/rentals            { movieId }  rent for three days",
        "POST   /api/screenings/{id}/checkout        book seats",
        "GET    /api/bookings/mine      your tickets",
    ];

    private static string[] AuthExample =>
    [
        "curl -X POST https://<site>/api/auth/login \\",
        "     -H \"Content-Type: application/json\" \\",
        "     -d '{\"email\":\"you@example.com\",\"password\":\"…\"}'",
        "",
        "curl https://<site>/api/rentals/mine -H \"Authorization: Bearer <accessToken>\"",
    ];

    private static string[] ErrorExample =>
    [
        "HTTP/1.1 400 Bad Request",
        "Content-Type: application/problem+json",
        "",
        "{ \"title\": \"One or more validation errors occurred.\",",
        "  \"status\": 400,",
        "  \"errors\": { \"Password\": [\"Password needs at least one digit.\"] } }",
    ];
}
