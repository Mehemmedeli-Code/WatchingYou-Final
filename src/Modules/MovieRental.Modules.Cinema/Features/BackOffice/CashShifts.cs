using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// Cash shifts. Nothing is sold at a till without one: the shift is what ties every ticket and
// every bar receipt to a person and a drawer. The X report is the running total mid-shift;
// closing the shift is the Z report, and it is final.

public sealed record ShiftSummary(
    Guid Id, string CashierName, DateTime OpenedAtUtc, DateTime? ClosedAtUtc, decimal OpeningFloat,
    int TicketsSold, decimal TicketCash, decimal TicketCard,
    int BarReceipts, decimal BarCash, decimal BarCard,
    decimal SalesCash, decimal SalesCard, decimal SalesTotal,
    decimal ExpectedCash, decimal? CountedCash, decimal? Variance, string? Note);

internal static class ShiftTotals
{
    public static async Task<IReadOnlyList<ShiftSummary>> ForAsync(
        CinemaDbContext db, IReadOnlyList<CashShift> shifts, CancellationToken ct)
    {
        if (shifts.Count == 0) return [];
        var ids = shifts.Select(s => s.Id).ToList();

        var tickets = await db.SeatPayments.AsNoTracking()
            .Where(p => p.ShiftId != null && ids.Contains(p.ShiftId.Value) && p.Status == PaymentStatus.Confirmed)
            .Select(p => new { ShiftId = p.ShiftId!.Value, p.Tender, p.Amount, Seats = p.Seats.Count })
            .ToListAsync(ct);

        var bar = await db.ConcessionSales.AsNoTracking()
            .Where(s => ids.Contains(s.ShiftId))
            .Select(s => new { s.ShiftId, s.Tender, s.Total })
            .ToListAsync(ct);

        return [.. shifts.Select(shift =>
        {
            var t = tickets.Where(x => x.ShiftId == shift.Id).ToList();
            var b = bar.Where(x => x.ShiftId == shift.Id).ToList();

            var ticketCash = t.Where(x => x.Tender == TenderType.Cash).Sum(x => x.Amount);
            var ticketCard = t.Where(x => x.Tender == TenderType.Card).Sum(x => x.Amount);
            var barCash = b.Where(x => x.Tender == TenderType.Cash).Sum(x => x.Total);
            var barCard = b.Where(x => x.Tender == TenderType.Card).Sum(x => x.Total);

            // A closed shift reports what was stored at closing, not a fresh sum: the Z report
            // is a record of that moment and must not move afterwards.
            var expected = shift.ExpectedCash ?? shift.OpeningFloat + ticketCash + barCash;

            return new ShiftSummary(
                shift.Id, shift.CashierName, shift.CreatedAtUtc, shift.ClosedAtUtc, shift.OpeningFloat,
                t.Sum(x => x.Seats), ticketCash, ticketCard,
                b.Count, barCash, barCard,
                ticketCash + barCash, ticketCard + barCard, ticketCash + barCash + ticketCard + barCard,
                expected, shift.CountedCash, shift.Variance, shift.Note);
        })];
    }

    public static async Task<ShiftSummary> ForAsync(CinemaDbContext db, CashShift shift, CancellationToken ct) =>
        (await ForAsync(db, [shift], ct))[0];
}

// ------------------------------------------------------------------ open

public sealed record OpenShiftCommand(decimal OpeningFloat) : ICommand<Result<ShiftSummary>>;

internal sealed class OpenShiftValidator : AbstractValidator<OpenShiftCommand>
{
    public OpenShiftValidator() =>
        RuleFor(x => x.OpeningFloat).InclusiveBetween(0, 10_000).WithMessage("The float is between 0 and 10 000.");
}

internal sealed class OpenShiftHandler(CinemaDbContext db, ICurrentUser currentUser, IUserDirectory users, IAuditLog audit)
    : ICommandHandler<OpenShiftCommand, Result<ShiftSummary>>
{
    public async Task<Result<ShiftSummary>> Handle(OpenShiftCommand command, CancellationToken ct)
    {
        var cashierId = currentUser.RequireId();
        if (await Shifts.OpenForAsync(db, cashierId, ct) is not null)
            return Result.Failure<ShiftSummary>(Error.Conflict("You already have an open shift. Close it first."));

        var name = (await users.GetContactAsync(cashierId, ct))?.FullName ?? currentUser.Email ?? "Cashier";
        var shift = new CashShift { CashierId = cashierId, CashierName = name, OpeningFloat = command.OpeningFloat };
        db.CashShifts.Add(shift);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two clicks racing past the check above; the filtered unique index kept one.
            return Result.Failure<ShiftSummary>(Error.Conflict("You already have an open shift."));
        }

        await audit.RecordAsync(new AuditEntry("shift.opened", name, $"Float {command.OpeningFloat:0.00}", shift.Id), ct);
        return Result.Success(await ShiftTotals.ForAsync(db, shift, ct));
    }
}

// ------------------------------------------------------------------ close (Z report)

public sealed record CloseShiftCommand(decimal CountedCash, string? Note) : ICommand<Result<ShiftSummary>>;

internal sealed class CloseShiftValidator : AbstractValidator<CloseShiftCommand>
{
    public CloseShiftValidator()
    {
        RuleFor(x => x.CountedCash).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

internal sealed class CloseShiftHandler(CinemaDbContext db, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<CloseShiftCommand, Result<ShiftSummary>>
{
    public async Task<Result<ShiftSummary>> Handle(CloseShiftCommand command, CancellationToken ct)
    {
        var shift = await Shifts.OpenForAsync(db, currentUser.RequireId(), ct);
        if (shift is null) return Result.Failure<ShiftSummary>(Error.NotFound("Open shift"));

        var running = await ShiftTotals.ForAsync(db, shift, ct);

        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.ExpectedCash = running.ExpectedCash;
        shift.CountedCash = command.CountedCash;
        shift.Variance = command.CountedCash - running.ExpectedCash;
        shift.Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        await db.SaveChangesAsync(ct);

        // A drawer that does not match is exactly the thing somebody will ask about later.
        await audit.RecordAsync(new AuditEntry("shift.closed", shift.CashierName,
            $"Expected {shift.ExpectedCash:0.00}, counted {shift.CountedCash:0.00}, variance {shift.Variance:+0.00;-0.00;0.00}" +
            (shift.Note is null ? "" : $" — {shift.Note}"), shift.Id), ct);

        return Result.Success(await ShiftTotals.ForAsync(db, shift, ct));
    }
}

// ------------------------------------------------------------------ reads

public sealed record GetMyShiftQuery : IQuery<ShiftSummary?>;

internal sealed class GetMyShiftHandler(CinemaDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyShiftQuery, ShiftSummary?>
{
    public async Task<ShiftSummary?> Handle(GetMyShiftQuery query, CancellationToken ct)
    {
        var shift = await Shifts.OpenForAsync(db, currentUser.RequireId(), ct);
        return shift is null ? null : await ShiftTotals.ForAsync(db, shift, ct);
    }
}

public sealed record GetShiftHistoryQuery(int Take) : IQuery<IReadOnlyList<ShiftSummary>>;

internal sealed class GetShiftHistoryHandler(CinemaDbContext db) : IQueryHandler<GetShiftHistoryQuery, IReadOnlyList<ShiftSummary>>
{
    public async Task<IReadOnlyList<ShiftSummary>> Handle(GetShiftHistoryQuery query, CancellationToken ct)
    {
        var shifts = await db.CashShifts.AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(Math.Clamp(query.Take, 1, 200))
            .ToListAsync(ct);
        return await ShiftTotals.ForAsync(db, shifts, ct);
    }
}

public static class CashShiftEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var till = app.MapGroup("/api/backoffice/shift").WithTags("Back office")
            .RequireAuthorization(AppPolicies.BackOffice);

        // 204 when no shift is open: "nothing" is a normal answer here, not an error.
        till.MapGet("", async Task<Results<Ok<ShiftSummary>, NoContent>> (IDispatcher dispatcher, CancellationToken ct) =>
                await dispatcher.Ask(new GetMyShiftQuery(), ct) is { } shift ? TypedResults.Ok(shift) : TypedResults.NoContent())
            .WithName("GetMyShift");

        till.MapPost("/open", async Task<Results<Ok<ShiftSummary>, Conflict<Error>, BadRequest<Error>>> (
                OpenShiftCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "conflict" ? TypedResults.Conflict(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("OpenShift");

        till.MapPost("/close", async Task<Results<Ok<ShiftSummary>, NotFound<Error>, BadRequest<Error>>> (
                CloseShiftCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("CloseShift");

        app.MapGet("/api/backoffice/shifts", async (int? take, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetShiftHistoryQuery(take ?? 50), ct)))
            .WithTags("Back office").RequireAuthorization(AppRoles.Admin).WithName("GetShiftHistory");
    }
}
