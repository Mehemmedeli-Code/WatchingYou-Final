using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Documents;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Rentals.Features;

// Rentals as a spreadsheet, for the admin who wants to do their own sums.

/// <param name="Pdf">A printable report instead of the spreadsheet.</param>
public sealed record ExportRentalsQuery(DateTime? FromUtc, DateTime? ToUtc, bool Pdf = false) : IQuery<byte[]>;

internal sealed class ExportRentalsHandler(RentalsDbContext db, IUserDirectory users)
    : IQueryHandler<ExportRentalsQuery, byte[]>
{
    public const int MaxRows = 5000;

    public async Task<byte[]> Handle(ExportRentalsQuery query, CancellationToken ct)
    {
        var from = query.FromUtc ?? DateTime.UtcNow.AddDays(-90);
        var to = query.ToUtc ?? DateTime.UtcNow.AddDays(1);
        var now = DateTime.UtcNow;

        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.RentedAtUtc >= from && r.RentedAtUtc < to)
            .OrderByDescending(r => r.RentedAtUtc)
            .Take(MaxRows)
            .ToListAsync(ct);

        var contacts = await users.GetContactsAsync([.. rentals.Select(r => r.UserId).Distinct()], ct);
        string Email(Guid id) => contacts.GetValueOrDefault(id)?.Email ?? "";
        if (query.Pdf) return RentalsPdf(rentals, Email, from, to, now);

        return Csv.Build(
            ["Film", "Customer", "Rented (UTC)", "Due (UTC)", "Returned (UTC)", "Status",
             "Days overdue", "Extensions", "Daily price", "Base price", "Late fee", "Total"],
            rentals.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.MovieTitle, Email(r.UserId), r.RentedAtUtc, r.DueAtUtc, r.ReturnedAtUtc,
                r.StatusAt(now).ToString(), r.DaysOverdue(r.ReturnedAtUtc ?? now), r.ExtensionCount,
                r.DailyPrice, r.BasePrice, r.LateFee, r.BasePrice + r.LateFee
            }));
    }

    /// <summary>The same rentals on paper: the columns a person reads, totals at the foot.</summary>
    private static byte[] RentalsPdf(List<Domain.Rental> rentals, Func<Guid, string> email, DateTime from, DateTime to, DateTime now)
    {
        // Baku has kept UTC+4 without daylight saving since 2016.
        static string Day(DateTime? utc) => utc is { } u ? u.AddHours(4).ToString("dd.MM.yy HH:mm", CultureInfo.InvariantCulture) : "-";
        static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        string Status(Domain.Rental r) => r.StatusAt(now).ToString() switch
        {
            "Active" => Lang.PickLatin("Aktiv", "Active", "Aktif"),
            "Overdue" => Lang.PickLatin("Gecikib", "Overdue", "Gecikmiş"),
            "Returned" => Lang.PickLatin("Qaytarılıb", "Returned", "Iade edildi"),
            var other => other
        };
        var rows = rentals.Select(r => (IReadOnlyList<string>)
        [
            r.MovieTitle, email(r.UserId), Day(r.RentedAtUtc), Day(r.DueAtUtc), Day(r.ReturnedAtUtc), Status(r),
            r.DaysOverdue(r.ReturnedAtUtc ?? now).ToString(CultureInfo.InvariantCulture), r.ExtensionCount.ToString(CultureInfo.InvariantCulture),
            Money(r.BasePrice), Money(r.LateFee), Money(r.BasePrice + r.LateFee),
        ]).ToList();
        return TablePdf.Render(
            Lang.PickLatin("Kirayələr", "Rentals", "Kiralamalar"),
            $"{from.AddHours(4):dd.MM.yyyy} - {to.AddHours(4):dd.MM.yyyy} · {rentals.Count} " +
                Lang.PickLatin("kirayə", "rentals", "kiralama") + " · " + Lang.PickLatin("vaxt: Bakı", "times: Baku", "saat: Bakü"),
            [
                new("Film", 30), new(Lang.PickLatin("Müştəri", "Customer", "Müşteri"), 30),
                new(Lang.PickLatin("Götürülüb", "Rented", "Alındı"), 14), new(Lang.PickLatin("Son tarix", "Due", "Son tarih"), 14),
                new(Lang.PickLatin("Qaytarılıb", "Returned", "Iade"), 14), new(Lang.PickLatin("Status", "Status", "Durum"), 11),
                new(Lang.PickLatin("Gecikmə", "Late days", "Gecikme"), 8, true), new(Lang.PickLatin("Uzadılıb", "Extended", "Uzatma"), 8, true),
                new(Lang.PickLatin("Qiymət", "Price", "Fiyat"), 8, true), new(Lang.PickLatin("Cərimə", "Late fee", "Ceza"), 8, true),
                new(Lang.PickLatin("Cəmi", "Total", "Toplam"), 8, true),
            ],
            rows,
            [Lang.PickLatin("CƏMİ", "TOTAL", "TOPLAM"), "", "", "", "", "", "", "",
             Money(rentals.Sum(r => r.BasePrice)), Money(rentals.Sum(r => r.LateFee)), Money(rentals.Sum(r => r.BasePrice + r.LateFee))],
            Lang.PickLatin("Səhifə", "Page", "Sayfa"));
    }
}

public static class ExportRentalsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/export/rentals.csv", async (DateTime? from, DateTime? to, IDispatcher dispatcher, CancellationToken ct) =>
                Results.File(await dispatcher.Ask(new ExportRentalsQuery(from, to), ct), Csv.ContentType,
                    $"watchingyou-rentals-{DateTime.UtcNow:yyyyMMdd}.csv"))
            .WithName("ExportRentals").WithTags("Rentals").RequireAuthorization(AppRoles.Admin);

    public static void MapPdf(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/export/rentals.pdf", async (DateTime? from, DateTime? to, IDispatcher dispatcher, CancellationToken ct) =>
                Results.File(await dispatcher.Ask(new ExportRentalsQuery(from, to, Pdf: true), ct), "application/pdf",
                    $"watchingyou-rentals-{DateTime.UtcNow:yyyyMMdd}.pdf"))
            .WithName("ExportRentalsPdf").WithTags("Rentals").RequireAuthorization(AppRoles.Admin);
}
