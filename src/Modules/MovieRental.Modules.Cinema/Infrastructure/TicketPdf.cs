using System.Globalization;
using System.Text;
using MovieRental.Modules.Cinema.Features;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>
/// Renders a booking as a PDF: one page per seat, each with its own QR code, so a family of
/// four can forward one page each and walk in separately.
///
/// Written by hand against the PDF 1.4 object model rather than pulling in a library. A ticket
/// is a few lines of text and a grid of squares; the standard Helvetica fonts every reader
/// ships with cover the text, and the QR code is drawn as filled rectangles. The result is a
/// few kilobytes and opens anywhere.
///
/// The built-in fonts only know Western European characters, so the handful of Azerbaijani and
/// Turkish letters outside that set (ə, ş, ğ, ı, İ) are written as their closest Latin letter.
/// </summary>
public static class TicketPdf
{
    private const float PageWidth = 360;
    private const float PageHeight = 560;

    public static byte[] Render(TicketResponse ticket, string? venue = null)
    {
        var pages = ticket.Seats.Count == 0
            ? [PageContent(ticket, null, venue)]
            : ticket.Seats.Select(seat => PageContent(ticket, seat, venue)).ToList();

        return Assemble(pages, $"WatchingYou ticket {ticket.Reference}");
    }

    // The ticket's own words. The standard PDF fonts draw Latin letters only (Azerbaijani and
    // Turkish ones are mapped to the nearest, see Latin), so a Russian ticket keeps English
    // labels rather than printing empty boxes.
    private sealed record Words(string Culture, string Ticket, string Date, string Time, string Baku, string Cinema, string Hall,
        string Seat, string RowSeat, string Audio, string Subtitles, string Reference, string Footer, string Paid, string Ending);

    private static Words WordsFor(string language) => language switch
    {
        "az" => new("az-Latn-AZ", "KİNO BİLETİ", "Tarix", "Saat", "Bakı", "Kinoteatr", "Zal", "Yer", "{0} sırası, {1} yer",
            "Səs", "altyazı", "Kod", "Bu kodu girişdə göstərin. Hər yerin öz kodu var.", "Ödənilib: {0} AZN", "{0}, son rəqəmlər {1}"),
        "tr" => new("tr-TR", "SİNEMA BİLETİ", "Tarih", "Saat", "Bakü", "Sinema", "Salon", "Koltuk", "{0} sırası, {1} numara",
            "Ses", "altyazı", "Kod", "Bu kodu kapıda gösterin. Her koltuğun kendi kodu vardır.", "Ödendi: {0} AZN", "{0}, son haneler {1}"),
        _ => new("en-GB", "CINEMA TICKET", "Date", "Time", "Baku", "Cinema", "Hall", "Seat", "Row {0}, seat {1}",
            "Audio", "subtitles", "Reference", "Show this code at the door. Each seat has its own code.", "Paid {0} AZN", "{0} ending {1}"),
    };

    private static string PageContent(TicketResponse ticket, TicketSeat? seat, string? venue)
    {
        var c = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        var w = WordsFor(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        // Header band.
        c.Append("0.07 0.07 0.09 rg\n");
        c.Append(inv, $"0 {PageHeight - 70} {PageWidth} 70 re f\n");
        Text(c, "F2", 16, 24, PageHeight - 38, "WATCHINGYOU", 1f);
        Text(c, "F1", 9, 24, PageHeight - 56, w.Ticket, 0.75f);
        if (seat is not null)
            TextRight(c, "F2", 22, PageWidth - 24, PageHeight - 46, seat.Label, 1f);

        // Film and screening.
        var y = PageHeight - 104f;
        foreach (var line in Wrap(ticket.MovieTitle, 30).Take(2))
        {
            Text(c, "F2", 17, 24, y, line, 0.07f);
            y -= 21;
        }

        y -= 6;
        var local = ToBaku(ticket.StartsAtUtc);
        Row(c, ref y, w.Date, local.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo(w.Culture)));
        Row(c, ref y, w.Time, local.ToString("HH:mm", inv) + $" ({w.Baku})");
        if (!string.IsNullOrWhiteSpace(venue)) Row(c, ref y, w.Cinema, venue);
        Row(c, ref y, w.Hall, ticket.Hall);
        Row(c, ref y, w.Seat, seat is null ? string.Join(", ", ticket.Seats.Select(s => s.Label)) : string.Format(inv, w.RowSeat, (char)('A' + seat.Row - 1), seat.Number));
        Row(c, ref y, w.Audio, ticket.AudioLanguage.ToUpperInvariant()
            + (string.IsNullOrWhiteSpace(ticket.SubtitleLanguage) ? "" : $" / {w.Subtitles} {ticket.SubtitleLanguage!.ToUpperInvariant()}"));
        Row(c, ref y, w.Reference, ticket.Reference);

        // QR code, centred in the space that is left.
        if (seat is not null)
        {
            var qr = QrCode.EncodeText(seat.QrPayload);
            const float qrSide = 150f;
            var module = qrSide / (qr.Size + 8);        // four modules of quiet zone each side
            var left = (PageWidth - qrSide) / 2;
            // Centred between the last detail row and the footer rule.
            var bottom = Math.Max(66f, 66f + (y - 66f - qrSide) / 2);

            c.Append("1 1 1 rg\n");
            c.Append(inv, $"{left:0.##} {bottom:0.##} {qrSide:0.##} {qrSide:0.##} re f\n");
            c.Append("0 0 0 rg\n");
            for (var qy = 0; qy < qr.Size; qy++)
                for (var qx = 0; qx < qr.Size; qx++)
                {
                    if (!qr[qx, qy]) continue;
                    var px = left + (qx + 4) * module;
                    var py = bottom + qrSide - (qy + 5) * module;
                    // A hair of overlap so no reader shows seams between squares.
                    c.Append(inv, $"{px:0.###} {py:0.###} {module + 0.05f:0.###} {module + 0.05f:0.###} re\n");
                }
            c.Append("f\n");
        }

        // Footer.
        c.Append("0.85 0.85 0.87 RG 0.8 w\n");
        c.Append(inv, $"24 58 m {PageWidth - 24} 58 l S\n");
        Text(c, "F1", 8, 24, 42, w.Footer, 0.35f);
        Text(c, "F1", 8, 24, 30,
            string.Format(inv, w.Paid, ticket.Amount.ToString("0.00", inv)) +
            (string.IsNullOrEmpty(ticket.Last4) ? "" : " · " + string.Format(inv, w.Ending, ticket.Brand, ticket.Last4)), 0.35f);

        return c.ToString();
    }

    private static void Row(StringBuilder c, ref float y, string label, string value)
    {
        Text(c, "F1", 8, 24, y, label.ToUpperInvariant(), 0.45f);
        Text(c, "F2", 11, 100, y, value, 0.1f);
        y -= 19;
    }

    private static void Text(StringBuilder c, string font, float size, float x, float y, string text, float grey)
    {
        var inv = CultureInfo.InvariantCulture;
        c.Append(inv, $"{grey:0.##} {grey:0.##} {grey:0.##} rg BT /{font} {size:0.#} Tf {x:0.##} {y:0.##} Td ({Escape(text)}) Tj ET\n");
    }

    /// <summary>Right-aligned by estimating Helvetica-Bold's average glyph width; the seat label
    /// is two or three characters, so the estimate is never visibly off.</summary>
    private static void TextRight(StringBuilder c, string font, float size, float right, float y, string text, float grey) =>
        Text(c, font, size, right - text.Length * size * 0.62f, y, text, grey);

    internal static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    private static DateTime ToBaku(DateTime utc)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        }
        catch (Exception)
        {
            return utc.AddHours(4);   // Azerbaijan has kept UTC+4 without daylight saving since 2016
        }
    }

    /// <summary>Maps text to the WinAnsi (Latin-1) range the standard fonts can draw, and
    /// escapes the three characters PDF strings treat specially.</summary>
    internal static string Escape(string text)
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

    private static byte[] Assemble(IReadOnlyList<string> pageContents, string title)
    {
        var latin1 = Encoding.Latin1;
        var objects = new List<string>();

        // 1 catalog, 2 page tree, 3 regular font, 4 bold font, 5 info; then page + content pairs.
        var pageIds = Enumerable.Range(0, pageContents.Count).Select(i => 6 + i * 2).ToArray();

        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Length} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        objects.Add($"<< /Title ({Escape(title)}) /Producer (WatchingYou) >>");

        for (var i = 0; i < pageContents.Count; i++)
        {
            var contentId = pageIds[i] + 1;
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>");
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
