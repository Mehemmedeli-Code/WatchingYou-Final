using System.Globalization;
using System.Text;

namespace MovieRental.SharedKernel.Documents;

/// <summary>
/// The smallest PDF writer that does the job: page content streams wrapped into a PDF 1.4
/// file with the standard fonts every reader ships with. No library, a few kilobytes out.
/// The built-in fonts only know Western European letters, so ə, ş, ğ, ı, İ are written as
/// their closest Latin letter.
/// </summary>
public static class Pdf
{
    /// <summary>Maps text to the WinAnsi (Latin-1) range the standard fonts can draw, and
    /// escapes the three characters PDF strings treat specially.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var mapped = ch switch
            {
                'ə' => 'e', 'Ə' => 'E', 'ş' => 's', 'Ş' => 'S', 'ğ' => 'g', 'Ğ' => 'G',
                'ı' => 'i', 'İ' => 'I', '·' => '·', '—' => '-', '–' => '-', '’' => '\'', '“' => '"', '”' => '"',
                _ => ch
            };
            if (mapped is '(' or ')' or '\\') sb.Append('\\');
            sb.Append(mapped <= 'ÿ' ? mapped : '?');
        }
        return sb.ToString();
    }

    /// <summary>Wraps page content streams into a PDF. Fonts: F1 Helvetica, F2 Helvetica-Bold,
    /// F3 Courier, F4 Courier-Bold (the monospaced pair lines up a receipt's columns).</summary>
    public static byte[] Assemble(IReadOnlyList<string> pageContents, string title,
        float pageWidth, float pageHeight)
    {
        var latin1 = Encoding.Latin1;
        var objects = new List<string>();

        // 1 catalog, 2 page tree, 3-4 Helvetica, 5 info, 6-7 Courier; then page + content pairs.
        var pageIds = Enumerable.Range(0, pageContents.Count).Select(i => 8 + i * 2).ToArray();

        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Length} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        objects.Add($"<< /Title ({Escape(title)}) /Producer (WatchingYou) >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>");

        for (var i = 0; i < pageContents.Count; i++)
        {
            var contentId = pageIds[i] + 1;
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth.ToString(CultureInfo.InvariantCulture)} {pageHeight.ToString("0.##", CultureInfo.InvariantCulture)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 6 0 R /F4 7 0 R >> >> /Contents {contentId} 0 R >>");
            var stream = pageContents[i];
            objects.Add($"<< /Length {latin1.GetByteCount(stream)} >>\nstream\n{stream}endstream");
        }

        using var output = new MemoryStream();
        void Write(string s) { var bytes = latin1.GetBytes(s); output.Write(bytes, 0, bytes.Length); }

        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = output.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = output.Position;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) sb.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(sb.ToString());

        return output.ToArray();
    }
}
