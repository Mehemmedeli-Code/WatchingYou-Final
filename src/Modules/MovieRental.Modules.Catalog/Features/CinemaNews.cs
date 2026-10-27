using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MovieRental.Modules.Catalog.Features;

/// <summary>One story in the paper: the headline, a few lines from the source, and a link to the
/// whole article there. Lang is the language it is written in.</summary>
public sealed record NewsStory(string Title, string? Summary, string Link, string Source, string? Image, DateTimeOffset? Published, string Lang);

/// <summary>Today's paper (Baku date) and its number, counted from the first issue.</summary>
public sealed record NewsEdition(DateOnly Date, int Number, IReadOnlyList<NewsStory> Stories, IReadOnlyList<string> Sources);

/// <summary>
/// The daily cinema paper: headlines from the film trade press's own RSS feeds, read again at
/// most once an hour. Only the headline, a short excerpt and the picture are shown; every story
/// links to the whole article at its source. Politics is left out by a word list.
/// </summary>
public static partial class CinemaNews
{
    public const string ClientName = "cinema-news";

    private sealed record Feed(string Source, string Url, string Lang);

    private static readonly Feed[] Feeds =
    [
        new("Variety", "https://variety.com/v/film/feed/", "en"),
        new("Deadline", "https://deadline.com/v/film/feed/", "en"),
        new("The Hollywood Reporter", "https://www.hollywoodreporter.com/c/movies/feed/", "en"),
        new("IndieWire", "https://www.indiewire.com/c/film/feed/", "en"),
        new("Collider", "https://collider.com/feed/category/movie-news/", "en"),
        new("Beyazperde", "https://www.beyazperde.com/rss/haberler.xml", "tr"),
        new("KinoNews", "https://www.kinonews.ru/rss/", "ru"),
        new("Film.ru", "https://www.film.ru/rss.xml", "ru"),
    ];

    // The feeds each site language reads, its own first. There is no Azerbaijani cinema feed;
    // Turkish is the one an Azerbaijani reader follows most easily.
    private static readonly Dictionary<string, string[]> Reads = new()
    {
        ["az"] = ["tr", "en"], ["tr"] = ["tr", "en"], ["ru"] = ["ru", "en"], ["en"] = ["en"],
    };

    private static readonly DateOnly FirstIssue = new(2026, 10, 27);
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, (DateTime At, List<NewsStory> Stories)> read = new();
    private static readonly SemaphoreSlim refreshing = new(1, 1);

    // Prefixes, so "politics", "seçimler" and "президента" all match. Not "war": that is Star Wars.
    // ponytail: a word list, not a classifier; add a word here when a political story slips through.
    [GeneratedRegex(@"\b(politic|election|trump|biden|government|minister|parliament|senat|congress|white house|gaza|israel|palestin|ukrain|putin|kremlin|protest|tariff|sanction|refugee|immigra|military|siyas|seçim|cumhurbaşkan|bakan[ıl]|meclis|hükümet|erdoğan|protesto|milletvekil|iktidar|muhalefet|полит|выбор|президент|правительств|министр|депутат|военн|санкци|путин|госдум|митинг|кремл|украин|израил)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Political();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static async Task<NewsEdition> Today(IHttpClientFactory http, string lang, CancellationToken ct)
    {
        var langs = Reads.GetValueOrDefault(lang) ?? Reads["en"];
        var feeds = Feeds.Where(f => langs.Contains(f.Lang)).ToList();
        await Refresh(http, feeds, ct);

        var since = DateTimeOffset.UtcNow.AddDays(-3);
        var stories = feeds
            .SelectMany(f => read.TryGetValue(f.Url, out var got) ? got.Stories : [])
            .Where(s => s.Published is null || s.Published >= since)
            .DistinctBy(s => s.Link)
            // The reader's own language first, then the newest.
            .OrderBy(s => Array.IndexOf(langs, s.Lang))
            .ThenByDescending(s => s.Published)
            .Take(40)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4));
        return new NewsEdition(today, today.DayNumber - FirstIssue.DayNumber + 1, stories,
            feeds.Select(f => f.Source).ToList());
    }

    private static async Task Refresh(IHttpClientFactory http, List<Feed> feeds, CancellationToken ct)
    {
        bool Stale(Feed f) => !read.TryGetValue(f.Url, out var got) || DateTime.UtcNow - got.At > Fresh;
        if (!feeds.Any(Stale)) return;

        await refreshing.WaitAsync(ct);
        try
        {
            await Task.WhenAll(feeds.Where(Stale).Select(async feed =>
            {
                try
                {
                    var client = http.CreateClient(ClientName);
                    await using var body = await client.GetStreamAsync(feed.Url, ct);
                    var stories = Parse(XDocument.Load(body), feed).ToList();
                    read[feed.Url] = (DateTime.UtcNow, stories);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
                {
                    // Unreachable for now: keep what was read last time and try again in ten minutes.
                    var old = read.TryGetValue(feed.Url, out var got) ? got.Stories : [];
                    read[feed.Url] = (DateTime.UtcNow - Fresh + TimeSpan.FromMinutes(10), old);
                }
            }));
        }
        finally { refreshing.Release(); }
    }

    private static IEnumerable<NewsStory> Parse(XDocument xml, Feed feed)
    {
        foreach (var item in xml.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            string? Field(string name) => item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

            var title = Clean(Field("title"));
            // Only web links: a feed could carry a "javascript:" one.
            if (title is null || !Uri.TryCreate(Field("link")?.Trim(), UriKind.Absolute, out var link)
                || link.Scheme is not ("https" or "http")) continue;

            var summary = Clean(Field("description"));
            if (Political().IsMatch(title) || (summary is not null && Political().IsMatch(summary))) continue;

            yield return new NewsStory(title, Cut(summary, 320), link.ToString(), feed.Source, Image(item), Date(Field("pubDate")), feed.Lang);
        }
    }

    private static string? Image(XElement item)
    {
        var url = item.Elements()
            .Where(e => e.Name.LocalName is "thumbnail" or "content"
                || (e.Name.LocalName == "enclosure" && (e.Attribute("type")?.Value ?? "").StartsWith("image/")))
            .Select(e => e.Attribute("url")?.Value)
            .FirstOrDefault(u => u is not null);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri.ToString() : null;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Some feeds escape their HTML twice: decode, strip, and once more.
        var plain = WebUtility.HtmlDecode(Tags().Replace(WebUtility.HtmlDecode(Tags().Replace(text, " ")), " "));
        plain = Spaces().Replace(plain, " ").Replace("[…]", "").Replace("[...]", "").Trim();
        return plain.Length == 0 ? null : plain;
    }

    private static string? Cut(string? text, int max)
    {
        if (text is null || text.Length <= max) return text;
        var end = text.LastIndexOf(' ', max);
        return text[..(end > 0 ? end : max)].TrimEnd(',', '.', ';', ':') + "…";
    }

    // RSS dates look like "Tue, 27 Oct 2026 13:00:00 +0000"; .NET wants a colon in the offset.
    private static DateTimeOffset? Date(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var fixedOffset = Regex.Replace(text.Trim(), @"([+-]\d\d)(\d\d)$", "$1:$2");
        return DateTimeOffset.TryParse(fixedOffset, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null;
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/news", async (string? lang, IHttpClientFactory http, HttpContext context, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "public, max-age=600";
                return TypedResults.Ok(await Today(http, lang ?? "en", ct));
            })
        .WithName("GetCinemaNews").WithTags("Catalog").AllowAnonymous()
        .WithSummary("Today's cinema paper: film news from the trade press, no politics, at most an hour old")
        .Produces<NewsEdition>(StatusCodes.Status200OK);
}
