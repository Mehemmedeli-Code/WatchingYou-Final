using System.Globalization;
using System.Text;
using MovieRental.Modules.Cinema.Domain;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>
/// The ticket sales of a period as a till receipt: one narrow white page (80 mm roll width) in
/// a monospaced font, totals first, then by film and by payment method, then every order on one
/// or two lines. Printed at the end of the day so the till can see the whole result at a glance
/// — the CSV holds the same rows for a spreadsheet.
/// </summary>
public static class SalesReceiptPdf
{
    private const float Width = 226;          // 80 mm
    private const float Margin = 12;
    private const float FontSize = 8.4f;
    private const float LineHeight = 11.5f;
    private const int Columns = 38;           // Courier at 8.4 pt across 202 pt

    public sealed record Line(string Text, bool Bold = false);

    private sealed record Words(string Title, string Period, string Printed, string Orders, string Tickets, string RefundedOrders,
        string Gross, string Promo, string Points, string Paid, string Refunded, string Net, string ByFilm, string ByMethod,
        string List, string RefundedMark, string Card, string Stripe, string BoxOffice, string Cash, string None);

    private static Words WordsFor(string language) => language switch
    {
        "az" => new("BİLET SATIŞI HESABATI", "Dövr", "Çap", "Sifarişlər", "Biletlər", "Qaytarılan sifariş",
            "Bilet dəyəri", "Promo endirimi", "Xal endirimi", "Ödənilib", "Qaytarılıb", "XALİS GƏLİR", "FİLMLƏR ÜZRƏ",
            "ÖDƏNİŞ ÜSULU", "SİFARİŞLƏR", "QAYTARILIB", "Onlayn kart", "Stripe", "Kassa", "nağd", "Bu dövrdə satış yoxdur."),
        "tr" => new("BİLET SATIŞ RAPORU", "Dönem", "Basım", "Siparişler", "Biletler", "İade edilen sipariş",
            "Bilet tutarı", "Promosyon indirimi", "Puan indirimi", "Ödendi", "İade edildi", "NET GELİR", "FİLMLERE GÖRE",
            "ÖDEME YÖNTEMİ", "SİPARİŞLER", "İADE", "Online kart", "Stripe", "Gişe", "nakit", "Bu dönemde satış yok."),
        _ => new("TICKET SALES REPORT", "Period", "Printed", "Orders", "Tickets", "Refunded orders",
            "Ticket value", "Promo discounts", "Points discounts", "Paid", "Refunded", "NET REVENUE", "BY FILM",
            "BY PAYMENT METHOD", "ORDERS", "REFUNDED", "Online card", "Stripe", "Box office", "cash", "No sales in this period."),
    };

    public static byte[] Render(IReadOnlyList<SeatPayment> payments, DateOnly fromDay, DateOnly toDay, DateTime printedUtc)
    {
        var lines = Lines(payments, fromDay, toDay, printedUtc, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var height = 34 + lines.Count * LineHeight + 24;

        var c = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        c.Append("1 1 1 rg\n").Append(inv, $"0 0 {Width} {height:0.##} re f\n");      // white paper
        var y = height - 30;
        foreach (var line in lines)
        {
            c.Append(inv, $"0 0 0 rg BT /{(line.Bold ? "F4" : "F3")} {FontSize:0.#} Tf {Margin} {y:0.##} Td ({TicketPdf.Escape(line.Text)}) Tj ET\n");
            y -= LineHeight;
        }
        return TicketPdf.Assemble([c.ToString()], "WatchingYou sales report", Width, height);
    }

    /// <summary>The receipt as lines of at most <see cref="Columns"/> characters. Separate from
    /// drawing so the arithmetic can be checked without reading a PDF.</summary>
    public static List<Line> Lines(IReadOnlyList<SeatPayment> payments, DateOnly fromDay, DateOnly toDay, DateTime printedUtc, string language)
    {
        var w = WordsFor(language);
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<Line>();
        void Add(string text, bool bold = false) => lines.Add(new Line(text.Length > Columns ? text[..Columns] : text, bold));
        void Rule(char ch = '-') => Add(new string(ch, Columns));
        void Pair(string label, string value, bool bold = false)
        {
            var room = Columns - value.Length - 1;
            Add((label.Length > room ? label[..room] : label).PadRight(room) + " " + value, bold);
        }
        static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
        string Center(string text) => text.Length >= Columns ? text : new string(' ', (Columns - text.Length) / 2) + text;

        Add(Center("WATCHINGYOU"), bold: true);
        Add(Center(w.Title), bold: true);
        Rule('=');
        Pair(w.Period, fromDay == toDay
            ? fromDay.ToString("dd.MM.yyyy", inv)
            : $"{fromDay.ToString("dd.MM.yyyy", inv)} - {toDay.ToString("dd.MM.yyyy", inv)}");
        Pair(w.Printed, Baku(printedUtc).ToString("dd.MM.yyyy HH:mm", inv));
        Rule();

        if (payments.Count == 0)
        {
            Add(w.None);
            Rule('=');
            return lines;
        }

        var refunded = payments.Where(p => p.Status == PaymentStatus.Refunded).ToList();
        var gross = payments.Sum(p => p.Subtotal);
        var promo = payments.Sum(p => p.PromoDiscount);
        var points = payments.Sum(p => p.PointsDiscount);
        var paid = payments.Sum(p => p.Amount);
        var back = payments.Sum(p => p.RefundedAmount ?? 0);

        Pair(w.Orders, payments.Count.ToString(inv));
        Pair(w.Tickets, payments.Sum(p => p.Seats.Count).ToString(inv));
        Pair(w.RefundedOrders, refunded.Count.ToString(inv));
        Rule();
        Pair(w.Gross, Money(gross));
        if (promo > 0) Pair(w.Promo, "-" + Money(promo));
        if (points > 0) Pair(w.Points, "-" + Money(points));
        Pair(w.Paid, Money(paid));
        if (back > 0) Pair(w.Refunded, "-" + Money(back));
        Rule('=');
        Pair(w.Net, Money(paid - back) + " AZN", bold: true);
        Rule('=');

        Add(w.ByFilm, bold: true);
        foreach (var film in payments.GroupBy(p => p.Screening?.MovieTitle ?? "-").OrderByDescending(g => g.Sum(p => p.Amount - (p.RefundedAmount ?? 0))))
        {
            var net = film.Sum(p => p.Amount - (p.RefundedAmount ?? 0));
            var value = $"{film.Sum(p => p.Seats.Count),3} {Money(net),9}";
            Pair(film.Key, value);
        }
        Rule();

        Add(w.ByMethod, bold: true);
        foreach (var method in payments.GroupBy(p => Method(p, w)).OrderBy(g => g.Key))
            Pair(method.Key, $"{method.Count(),3} {Money(method.Sum(p => p.Amount - (p.RefundedAmount ?? 0))),9}");
        Rule();

        Add(w.List, bold: true);
        foreach (var p in payments.OrderBy(p => p.ConfirmedAtUtc ?? p.CreatedAtUtc))
        {
            var at = Baku(p.ConfirmedAtUtc ?? p.CreatedAtUtc).ToString("HH:mm", inv);
            Pair($"{p.Reference} {at}", Money(p.Amount));
            var seats = string.Join(" ", p.Seats.OrderBy(s => s.Row).ThenBy(s => s.Number).Select(s => $"{(char)('A' + s.Row - 1)}{s.Number}"));
            Add($"  {p.Screening?.MovieTitle ?? "-"} · {seats}");
            if (p.Status == PaymentStatus.Refunded) Pair($"  {w.RefundedMark}", "-" + Money(p.RefundedAmount ?? 0));
        }
        Rule('=');
        Add(Center($"{w.Orders} {payments.Count} · {w.Tickets} {payments.Sum(p => p.Seats.Count)}"));
        return lines;
    }

    private static string Method(SeatPayment p, Words w) => p.Provider switch
    {
        PaymentProvider.Stripe => w.Stripe,
        PaymentProvider.BoxOffice => p.Tender == TenderType.Cash ? $"{w.BoxOffice} ({w.Cash})" : w.BoxOffice,
        _ => w.Card,
    };

    internal static DateTime Baku(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(4);
}
