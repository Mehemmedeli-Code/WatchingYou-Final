using System.Text;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Infrastructure;

namespace MovieRental.Tests;

/// <summary>
/// The end-of-day receipt is read as the till's result, so its sums have to be the sums of the
/// orders on it, and every line has to fit the paper.
/// </summary>
public class SalesReceiptTests
{
    private static readonly DateOnly Day = new(2026, 10, 8);
    private static readonly DateTime Noon = new(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc);

    private static SeatPayment Order(string reference, string film, int seats, decimal subtotal, decimal paid,
        PaymentProvider provider = PaymentProvider.Card, decimal promo = 0, decimal points = 0, decimal? refunded = null) => new()
    {
        Reference = reference, Last4 = "4242", CardHolder = "T", CodeHash = "h", Salt = "s",
        Subtotal = subtotal, PromoDiscount = promo, PointsDiscount = points, Amount = paid, Provider = provider,
        Tender = provider == PaymentProvider.BoxOffice ? TenderType.Cash : null,
        Status = refunded is null ? PaymentStatus.Confirmed : PaymentStatus.Refunded,
        RefundedAmount = refunded, ConfirmedAtUtc = Noon,
        Screening = new Screening { MovieTitle = film, Hall = "C100", StartsAtUtc = Noon.AddHours(6) },
        Seats = [.. Enumerable.Range(1, seats).Select(n => new SeatBooking { Row = 9, Number = n, PricePaid = subtotal / seats })],
    };

    private static readonly SeatPayment[] Orders =
    [
        Order("WY-AAA111", "Alien", 2, 20m, 19.05m, points: 0.95m),
        Order("WY-BBB222", "Alien", 1, 10m, 8m, promo: 2m),
        Order("WY-CCC333", "Blue Hour", 3, 24m, 24m, PaymentProvider.BoxOffice),
        Order("WY-DDD444", "Blue Hour", 1, 8m, 8m, refunded: 5.60m),
    ];

    private static string Find(List<SalesReceiptPdf.Line> lines, string label) =>
        lines.Single(l => l.Text.StartsWith(label, StringComparison.Ordinal)).Text[label.Length..].Trim();

    [Fact]
    public void Totals_are_the_sums_of_the_orders()
    {
        var lines = SalesReceiptPdf.Lines(Orders, Day, Day, Noon, "en");

        Assert.Equal("4", Find(lines, "Orders"));
        Assert.Equal("7", Find(lines, "Tickets"));
        Assert.Equal("1", Find(lines, "Refunded orders"));
        Assert.Equal("62.00", Find(lines, "Ticket value"));
        Assert.Equal("-2.00", Find(lines, "Promo discounts"));
        Assert.Equal("-0.95", Find(lines, "Points discounts"));
        Assert.Equal("59.05", Find(lines, "Paid"));
        Assert.Equal("-5.60", Find(lines, "Refunded  "));
        Assert.Equal("53.45 AZN", Find(lines, "NET REVENUE"));
    }

    [Fact]
    public void Every_line_fits_the_roll()
    {
        var long_ = Order("WY-EEE555", "A Film With A Title Far Too Long For Any Receipt Roll", 1, 8m, 8m);
        var lines = SalesReceiptPdf.Lines([.. Orders, long_], Day, Day.AddDays(6), Noon, "az");
        Assert.All(lines, l => Assert.True(l.Text.Length <= 38, l.Text));
    }

    [Fact]
    public void An_empty_day_says_so()
    {
        var lines = SalesReceiptPdf.Lines([], Day, Day, Noon, "en");
        Assert.Contains(lines, l => l.Text == "No sales in this period.");
    }

    [Fact]
    public void The_pdf_is_one_narrow_page()
    {
        var pdf = Encoding.Latin1.GetString(SalesReceiptPdf.Render(Orders, Day, Day, Noon));
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
        Assert.Contains("/Count 1", pdf);
        Assert.Contains("/MediaBox [0 0 226 ", pdf);
        Assert.Contains("/BaseFont /Courier", pdf);
    }
}
