using System.Globalization;
using System.Text;

namespace MovieRental.SharedKernel.Documents;

/// <summary>
/// CSV for admin exports, written for Excel as much as for a parser.
///
/// Three details matter. A UTF-8 byte-order mark, or Excel reads "Bakı" as mojibake. Quoting
/// for any field with a comma, quote or line break. And formula injection: a customer who names
/// themselves "=HYPERLINK(...)" would otherwise become a live formula in the admin's
/// spreadsheet, so any field starting with = + - @ gets a leading apostrophe.
/// </summary>
public static class Csv
{
    public const string ContentType = "text/csv; charset=utf-8";

    public static byte[] Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Field)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(value => Field(Format(value)))));

        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. bom, .. body];
    }

    public static string Format(object? value) => value switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        double x => x.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "yes" : "no",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    public static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' && !IsNumber(value))
            value = "'" + value;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    // A plain negative number such as "-2.50" is data, not a formula.
    private static bool IsNumber(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
}
