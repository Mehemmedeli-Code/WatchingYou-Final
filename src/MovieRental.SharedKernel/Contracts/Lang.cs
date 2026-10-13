using System.Globalization;

namespace MovieRental.SharedKernel.Contracts;

/// <summary>
/// The reader's language for text the server writes itself — e-mails, SMS, PDFs. The host
/// sets the request's UI culture from the visitor's language choice; outside a request
/// (background jobs) it falls back to Azerbaijani, the site's default.
/// </summary>
public static class Lang
{
    public static string Code => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "en" => "en",
        "ru" => "ru",
        "tr" => "tr",
        _ => "az"
    };

    /// <summary>For PDFs: their built-in fonts have no Cyrillic, so Russian readers get English.</summary>
    public static string PickLatin(string az, string en, string tr) => Pick(az, en, en, tr);

    /// <summary>One text in each language, in the order az, en, ru, tr.</summary>
    public static string Pick(string az, string en, string ru, string tr) => Code switch
    {
        "en" => en,
        "ru" => ru,
        "tr" => tr,
        _ => az
    };
}
