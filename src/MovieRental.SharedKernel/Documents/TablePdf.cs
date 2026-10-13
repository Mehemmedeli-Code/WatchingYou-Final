using System.Globalization;
using System.Text;

namespace MovieRental.SharedKernel.Documents;

/// <summary>A column: its heading, its width in characters, and whether numbers line up on the right.</summary>
public sealed record PdfColumn(string Heading, int Width, bool Right = false);

/// <summary>
/// A report as a printable A4 page (landscape): a title, a line saying what it covers, one row
/// per record in a monospaced grid so the columns line up, the headings again on every page,
/// a totals row at the end, and "page n of m" at the foot. For admins who want paper, not a
/// spreadsheet.
/// </summary>
public static class TablePdf
{
    private const float Width = 842, Height = 595, Margin = 28, Size = 7.5f, Line = 11f;
    private const float CharWidth = Size * 0.6f;              // Courier: every glyph is 0.6 em
    public const int MaxChars = (int)((Width - 2 * Margin) / CharWidth);

    public static byte[] Render(string title, string subtitle, IReadOnlyList<PdfColumn> columns,
        IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<string>? totals = null, string pageWord = "Page")
    {
        var inv = CultureInfo.InvariantCulture;
        var perPage = (int)((Height - 2 * Margin - 70) / Line);
        var pageCount = Math.Max(1, (int)Math.Ceiling((rows.Count + (totals is null ? 0 : 2)) / (double)perPage));
        var pages = new List<string>();

        string Cell(string text, PdfColumn column)
        {
            var t = (text ?? "").Replace('\n', ' ');
            if (t.Length > column.Width) t = t[..Math.Max(0, column.Width - 1)] + "~";
            return column.Right ? t.PadLeft(column.Width) : t.PadRight(column.Width);
        }
        string Join(IReadOnlyList<string> cells) => string.Join(" ", columns.Select((c, i) => Cell(i < cells.Count ? cells[i] : "", c)));
        void Text(StringBuilder c, string font, float size, float x, float y, string text, float grey = 0) =>
            c.Append(inv, $"{grey:0.##} {grey:0.##} {grey:0.##} rg BT /{font} {size:0.#} Tf {x:0.##} {y:0.##} Td ({Pdf.Escape(text)}) Tj ET\n");

        var rowIndex = 0;
        for (var page = 1; page <= pageCount; page++)
        {
            var c = new StringBuilder();
            var y = Height - Margin - 14;
            Text(c, "F2", 15, Margin, y, title);
            Text(c, "F1", 9, Margin, y - 16, subtitle, 0.35f);
            y -= 44;
            Text(c, "F4", Size, Margin, y, Join([.. columns.Select(col => col.Heading)]));
            c.Append(inv, $"0.6 0.6 0.6 RG 0.5 w {Margin} {y - 4:0.##} m {Width - Margin} {y - 4:0.##} l S\n");
            y -= Line + 4;

            for (var n = 0; n < perPage && rowIndex < rows.Count; n++, rowIndex++)
            {
                if (rowIndex % 2 == 1)   // zebra rows: easier to follow one line across a wide page
                    c.Append(inv, $"0.95 0.95 0.95 rg {Margin - 2} {y - 3:0.##} {Width - 2 * Margin + 4} {Line:0.##} re f\n");
                Text(c, "F3", Size, Margin, y, Join(rows[rowIndex]));
                y -= Line;
            }

            if (page == pageCount && totals is not null)
            {
                c.Append(inv, $"0 0 0 RG 0.8 w {Margin} {y + 6:0.##} m {Width - Margin} {y + 6:0.##} l S\n");
                Text(c, "F4", Size, Margin, y - 4, Join(totals));
            }

            Text(c, "F1", 8, Width - Margin - 60, Margin - 8, $"{pageWord} {page} / {pageCount}", 0.45f);
            Text(c, "F1", 8, Margin, Margin - 8, "WatchingYou", 0.45f);
            pages.Add(c.ToString());
        }
        return Pdf.Assemble(pages, title, Width, Height);
    }
}
