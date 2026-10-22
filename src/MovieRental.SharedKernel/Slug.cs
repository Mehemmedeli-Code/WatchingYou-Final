using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieRental.SharedKernel;

/// <summary>Text as a URL- and file-safe name: "Two Weeks in Şəki" → "two-weeks-in-seki".
/// Film slugs (SlugFactory) and cinema photo files (VenuePhotos) both come from here.</summary>
public static partial class Slug
{
    public static string From(string text)
    {
        // Letters Unicode does not decompose into "base + accent", so the fold below would
        // drop them entirely: "Şəki" came out as "s-ki". Mapped by hand first. The ampersand is
        // spelled out, because "dust-copper" reads as a different title from "Dust & Copper".
        var spelled = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            spelled.Append(ch switch
            {
                'ə' or 'Ə' => "e",
                'ı' => "i",
                'İ' => "I",
                '&' => " and ",
                _ => ch.ToString()
            });
        }

        var normalized = spelled.ToString().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);

        return NonAlphanumeric().Replace(builder.ToString().ToLowerInvariant(), "-").Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
