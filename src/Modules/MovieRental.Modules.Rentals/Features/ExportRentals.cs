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

public sealed record ExportRentalsQuery(DateTime? FromUtc, DateTime? ToUtc) : IQuery<byte[]>;

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
}

public static class ExportRentalsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/export/rentals.csv", async (DateTime? from, DateTime? to, IDispatcher dispatcher, CancellationToken ct) =>
                Results.File(await dispatcher.Ask(new ExportRentalsQuery(from, to), ct), Csv.ContentType,
                    $"watchingyou-rentals-{DateTime.UtcNow:yyyyMMdd}.csv"))
            .WithName("ExportRentals").WithTags("Rentals").RequireAuthorization(AppRoles.Admin);
}
