using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieRental.Modules.Catalog.Domain;

public static partial class SlugFactory
{
    public static string Create(string title, int year)
    {
        var normalized = title.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);

        var ascii = NonAlphanumeric().Replace(builder.ToString().ToLowerInvariant(), "-").Trim('-');
        return $"{ascii}-{year}";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
