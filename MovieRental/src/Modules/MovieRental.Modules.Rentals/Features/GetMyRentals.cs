using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Rentals.Domain;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Rentals.Features;

// Feature 6 — personal history: active, returned and overdue in one place.
public readonly record struct RentalHistoryRequest(string? Status, int? Page, int? PageSize);

public sealed record GetMyRentalsQuery(RentalHistoryRequest Filter) : IQuery<PagedResult<RentalResponse>>;

internal sealed class GetMyRentalsHandler(RentalsDbContext db, ICurrentUser currentUser, LateFeePolicy policy)
    : IQueryHandler<GetMyRentalsQuery, PagedResult<RentalResponse>>
{
    public async Task<PagedResult<RentalResponse>> Handle(GetMyRentalsQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var page = Math.Max(1, query.Filter.Page ?? 1);
        var pageSize = Math.Clamp(query.Filter.PageSize ?? 10, 1, 50);
        var userId = currentUser.RequireId();

        var rentals = db.Rentals.AsNoTracking().Where(r => r.UserId == userId);

        rentals = query.Filter.Status?.ToLowerInvariant() switch
        {
            "active" => rentals.Where(r => r.ReturnedAtUtc == null && r.DueAtUtc >= now),
            "overdue" => rentals.Where(r => r.ReturnedAtUtc == null && r.DueAtUtc < now),
            "returned" => rentals.Where(r => r.ReturnedAtUtc != null),
            _ => rentals
        };

        var total = await rentals.CountAsync(ct);
        var page_ = await rentals
            .OrderByDescending(r => r.RentedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        // Late fees are projected in memory: the policy is domain logic, not SQL, and a
        // page of ten rows makes the round trip irrelevant.
        var items = page_.Select(r => r.ToResponse(policy, now)).ToList();
        return new PagedResult<RentalResponse>(items, page, pageSize, total);
    }
}

public sealed record GetOverdueRentalsQuery : IQuery<IReadOnlyList<RentalResponse>>;

internal sealed class GetOverdueRentalsHandler(RentalsDbContext db, LateFeePolicy policy)
    : IQueryHandler<GetOverdueRentalsQuery, IReadOnlyList<RentalResponse>>
{
    public async Task<IReadOnlyList<RentalResponse>> Handle(GetOverdueRentalsQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdue = await db.Rentals.AsNoTracking()
            .Where(r => r.ReturnedAtUtc == null && r.DueAtUtc < now)
            .OrderBy(r => r.DueAtUtc)
            .ToListAsync(ct);

        return overdue.Select(r => r.ToResponse(policy, now)).ToList();
    }
}

public sealed record RentalStatsResponse(
    int ActiveCount, int OverdueCount, int ReturnedCount, decimal OutstandingLateFees, decimal RevenueThisMonth);

public sealed record GetRentalStatsQuery : IQuery<RentalStatsResponse>;

internal sealed class GetRentalStatsHandler(RentalsDbContext db, LateFeePolicy policy)
    : IQueryHandler<GetRentalStatsQuery, RentalStatsResponse>
{
    public async Task<RentalStatsResponse> Handle(GetRentalStatsQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var open = await db.Rentals.AsNoTracking().Where(r => r.ReturnedAtUtc == null).ToListAsync(ct);
        var returnedCount = await db.Rentals.AsNoTracking().CountAsync(r => r.ReturnedAtUtc != null, ct);
        var revenue = await db.Rentals.AsNoTracking()
            .Where(r => r.RentedAtUtc >= monthStart)
            .SumAsync(r => r.BasePrice + r.LateFee, ct);

        return new RentalStatsResponse(
            open.Count(r => r.DueAtUtc >= now),
            open.Count(r => r.DueAtUtc < now),
            returnedCount,
            Math.Round(open.Sum(r => policy.Calculate(r, now)), 2),
            Math.Round(revenue, 2));
    }
}

public static class RentalHistoryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/rentals/mine", async (
                [AsParameters] RentalHistoryRequest filter, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetMyRentalsQuery(filter), ct)))
            .WithName("GetMyRentals").WithTags("Rentals").RequireAuthorization();

        app.MapGet("/api/admin/rentals/overdue", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetOverdueRentalsQuery(), ct)))
            .WithName("GetOverdueRentals").WithTags("Rentals").RequireAuthorization(AppRoles.Admin);

        app.MapGet("/api/admin/rentals/stats", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetRentalStatsQuery(), ct)))
            .WithName("GetRentalStats").WithTags("Rentals").RequireAuthorization(AppRoles.Admin);
    }
}
