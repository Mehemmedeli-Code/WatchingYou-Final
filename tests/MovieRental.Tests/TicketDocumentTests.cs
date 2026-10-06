using System.Text;
using MovieRental.Modules.Cinema.Features;
using MovieRental.Modules.Cinema.Infrastructure;
using MovieRental.SharedKernel.Documents;

namespace MovieRental.Tests;

/// <summary>The PDF ticket and its QR code are written by hand, so their structure is pinned
/// here. (The QR output was also checked against a real scanner library while it was built.)</summary>
public class TicketDocumentTests
{
    [Theory]
    [InlineData(1, 21)]
    [InlineData(7, 45)]
    [InlineData(10, 57)]
    public void Qr_size_follows_the_version(int version, int size) =>
        Assert.Equal(size, version * 4 + 17);

    [Fact]
    public void Qr_picks_the_smallest_version_that_fits()
    {
        Assert.Equal(1, QrCode.EncodeText("HELLO").Version);
        Assert.Equal(4, QrCode.EncodeText("WATCHINGYOU|WY-ABC234|0123456789abcdef0123456789abcdef|A12").Version);
        Assert.Throws<ArgumentException>(() => QrCode.EncodeText(new string('x', 300)));
    }

    [Fact]
    public void Qr_has_finder_patterns_in_three_corners()
    {
        var qr = QrCode.EncodeText("WATCHINGYOU");
        // The centre of each finder is dark, its separator ring (distance 4) light.
        Assert.True(qr[3, 3]);
        Assert.True(qr[qr.Size - 4, 3]);
        Assert.True(qr[3, qr.Size - 4]);
        Assert.False(qr[7, 7]);
    }

    [Fact]
    public void Pdf_has_one_page_per_seat_and_a_valid_trailer()
    {
        var ticket = new TicketResponse(Guid.NewGuid(), "WY-ABC234", "Blue Hour", "A100",
            new DateTime(2026, 10, 3, 16, 30, 0, DateTimeKind.Utc), "az", "en",
            [new TicketSeat(1, 5, "A5", "WATCHINGYOU|WY-ABC234|x|A5"), new TicketSeat(2, 6, "B6", "WATCHINGYOU|WY-ABC234|x|B6")],
            17m, "Visa", "4242", DateTime.UtcNow);

        var pdf = TicketPdf.Render(ticket, "Nizami");
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Count 2", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("(Blue Hour) Tj", text);
    }

    [Fact]
    public void Pdf_text_is_escaped_and_mapped_to_the_standard_fonts()
    {
        Assert.Equal(@"Seher \(yeni\) \\ ?", TicketPdf.Escape("Şəhər (yeni) \\ 中"));
    }

    [Fact]
    public void Csv_neutralises_formulas_but_keeps_negative_numbers()
    {
        // Starts with "=", so it gets an apostrophe; contains quotes, so it is quoted too.
        Assert.Equal("\"'=HYPERLINK(\"\"x\"\")\"", Csv.Field("=HYPERLINK(\"x\")"));
        Assert.Equal("-2.50", Csv.Field("-2.50"));
        Assert.Equal("'@admin", Csv.Field("@admin"));
        Assert.Equal("\"a,b\"", Csv.Field("a,b"));
    }

    [Fact]
    public void Csv_starts_with_a_byte_order_mark_for_excel()
    {
        var bytes = Csv.Build(["A"], [new object?[] { "Bakı" }]);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }
}
