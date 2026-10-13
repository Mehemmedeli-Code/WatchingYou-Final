using System.Text;
using MovieRental.SharedKernel.Documents;

namespace MovieRental.Tests;

/// <summary>The admin's printable reports: long lists break onto more pages, each with the
/// headings again, and Azerbaijani letters come out as readable Latin ones.</summary>
public sealed class TablePdfTests
{
    private static readonly PdfColumn[] Columns = [new("Film", 20), new("Total", 8, Right: true)];

    [Fact]
    public void A_long_list_runs_onto_more_pages_with_the_headings_on_each()
    {
        var rows = Enumerable.Range(1, 120).Select(i => (IReadOnlyList<string>)[$"Film {i}", "1.00"]).ToList();
        var text = Encoding.Latin1.GetString(TablePdf.Render("Rentals", "test", Columns, rows, ["TOTAL", "120.00"]));

        Assert.StartsWith("%PDF-1.4", text);
        var pages = System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page /Parent").Count;
        Assert.True(pages >= 3, $"{pages} pages");
        Assert.Equal(pages, System.Text.RegularExpressions.Regex.Matches(text, @"\(Film +Total\)").Count);
        Assert.Contains($"Page {pages} / {pages}", text);
        Assert.Contains("120.00", text);
    }

    [Fact]
    public void Long_cells_are_cut_and_azerbaijani_letters_are_kept_readable()
    {
        var text = Encoding.Latin1.GetString(TablePdf.Render("Kirayələr", "", Columns,
            [["Şəhərdə uzun bir film adı belə gəlir", "9.50"]]));

        Assert.Contains("(Kirayeler)", text);
        Assert.Contains("Seherde uzun bir fi~", text);   // 20 characters, the last one marks the cut
    }
}
