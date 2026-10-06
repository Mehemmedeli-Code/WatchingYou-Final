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

// The bar: popcorn, drinks, combos. For many cinemas this is where the margin is — the box
// office is shared with the distributor, the popcorn is not.

public sealed record ConcessionItemDto(
    Guid Id, string Name, ConcessionCategory Category, decimal Price, decimal CostPrice,
    int Stock, int LowStockThreshold, bool IsActive, bool IsLow);

internal static class ConcessionMapping
{
    public static ConcessionItemDto ToDto(this ConcessionItem i) =>
        new(i.Id, i.Name, i.Category, i.Price, i.CostPrice, i.Stock, i.LowStockThreshold, i.IsActive,
            i.Stock <= i.LowStockThreshold);
}

// ------------------------------------------------------------------ menu (manager)

public sealed record SaveConcessionItemCommand(
    Guid? Id, string Name, ConcessionCategory Category, decimal Price, decimal CostPrice,
    int Stock, int LowStockThreshold, bool IsActive) : ICommand<Result<ConcessionItemDto>>;

internal sealed class SaveConcessionItemValidator : AbstractValidator<SaveConcessionItemCommand>
{
    public SaveConcessionItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
    }
}

internal sealed class SaveConcessionItemHandler(CinemaDbContext db)
    : ICommandHandler<SaveConcessionItemCommand, Result<ConcessionItemDto>>
{
    public async Task<Result<ConcessionItemDto>> Handle(SaveConcessionItemCommand command, CancellationToken ct)
    {
        var item = command.Id is { } id ? await db.ConcessionItems.FirstOrDefaultAsync(i => i.Id == id, ct) : null;
        if (command.Id is not null && item is null) return Result.Failure<ConcessionItemDto>(Error.NotFound("Item"));

        if (item is null)
        {
            item = new ConcessionItem { Name = command.Name.Trim() };
            db.ConcessionItems.Add(item);
        }

        item.Name = command.Name.Trim();
        item.Category = command.Category;
        item.Price = command.Price;
        item.CostPrice = command.CostPrice;
        item.Stock = command.Stock;
        item.LowStockThreshold = command.LowStockThreshold;
        item.IsActive = command.IsActive;
        await db.SaveChangesAsync(ct);
        return Result.Success(item.ToDto());
    }
}

public sealed record RestockCommand(Guid Id, int Quantity) : ICommand<Result<ConcessionItemDto>>;

internal sealed class RestockHandler(CinemaDbContext db, IAuditLog audit) : ICommandHandler<RestockCommand, Result<ConcessionItemDto>>
{
    public async Task<Result<ConcessionItemDto>> Handle(RestockCommand command, CancellationToken ct)
    {
        if (command.Quantity is <= 0 or > 100_000)
            return Result.Failure<ConcessionItemDto>(Error.Validation("Restock by a positive quantity."));

        // Relative, not "set to": a sale happening while the delivery is being keyed in must
        // not be overwritten by a stale total.
        var changed = await db.ConcessionItems.Where(i => i.Id == command.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Stock, i => i.Stock + command.Quantity), ct);
        if (changed == 0) return Result.Failure<ConcessionItemDto>(Error.NotFound("Item"));

        var item = await db.ConcessionItems.AsNoTracking().FirstAsync(i => i.Id == command.Id, ct);
        await audit.RecordAsync(new AuditEntry("bar.restocked", item.Name, $"+{command.Quantity} → {item.Stock}", item.Id), ct);
        return Result.Success(item.ToDto());
    }
}

// ------------------------------------------------------------------ sale (till)

public sealed record BarLine(Guid ItemId, int Quantity);

public sealed record BarSaleCommand(IReadOnlyList<BarLine> Lines, TenderType Tender, decimal? CashReceived)
    : ICommand<Result<BarSaleResult>>;

public sealed record BarReceiptLine(string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record BarSaleResult(
    Guid Id, string Reference, decimal Total, decimal? Change, TenderType Tender,
    DateTime AtUtc, IReadOnlyList<BarReceiptLine> Lines);

internal sealed class BarSaleValidator : AbstractValidator<BarSaleCommand>
{
    public BarSaleValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("The basket is empty.");
        RuleForEach(x => x.Lines).Must(l => l.Quantity is > 0 and <= 50).WithMessage("Quantities run from 1 to 50.");
        RuleFor(x => x.Tender).IsInEnum();
    }
}

internal sealed class BarSaleHandler(CinemaDbContext db, ICurrentUser currentUser)
    : ICommandHandler<BarSaleCommand, Result<BarSaleResult>>
{
    public async Task<Result<BarSaleResult>> Handle(BarSaleCommand command, CancellationToken ct)
    {
        var shift = await Shifts.OpenForAsync(db, currentUser.RequireId(), ct);
        if (shift is null) return Result.Failure<BarSaleResult>(Error.Conflict("Open a cash shift before selling."));

        // Same item scanned twice is one line with the quantities added.
        var lines = command.Lines
            .GroupBy(l => l.ItemId)
            .Select(g => new BarLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        var ids = lines.Select(l => l.ItemId).ToList();
        var items = await db.ConcessionItems.AsNoTracking()
            .Where(i => ids.Contains(i.Id) && i.IsActive)
            .ToDictionaryAsync(i => i.Id, ct);
        if (items.Count != ids.Count)
            return Result.Failure<BarSaleResult>(Error.Validation("One of those items is not on the menu."));

        var total = lines.Sum(l => items[l.ItemId].Price * l.Quantity);
        decimal? change = null;
        if (command.Tender == TenderType.Cash)
        {
            var received = command.CashReceived ?? total;
            if (received < total)
                return Result.Failure<BarSaleResult>(Error.Validation($"Cash received is short of {total:0.00}."));
            change = received - total;
        }

        // Stock and receipt in one transaction. Each decrement is conditional on enough being
        // left, so two tills selling the last cola cannot both succeed; if any line falls
        // short, nothing is taken and nothing is recorded.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var line in lines)
        {
            var taken = await db.ConcessionItems
                .Where(i => i.Id == line.ItemId && i.Stock >= line.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Stock, i => i.Stock - line.Quantity), ct);

            if (taken == 0)
            {
                await tx.RollbackAsync(ct);
                var name = items[line.ItemId].Name;
                return Result.Failure<BarSaleResult>(Error.Conflict($"Not enough {name} in stock."));
            }
        }

        var sale = new ConcessionSale
        {
            ShiftId = shift.Id,
            Reference = BackOfficeReference.ForBar(),
            Tender = command.Tender,
            Total = total,
            Cost = lines.Sum(l => items[l.ItemId].CostPrice * l.Quantity),
            Lines = [.. lines.Select(l =>
            {
                var item = items[l.ItemId];
                return new ConcessionSaleLine
                {
                    ItemId = item.Id, Name = item.Name, Category = item.Category,
                    UnitPrice = item.Price, UnitCost = item.CostPrice, Quantity = l.Quantity
                };
            })]
        };
        db.ConcessionSales.Add(sale);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result.Success(new BarSaleResult(
            sale.Id, sale.Reference, sale.Total, change, sale.Tender, sale.CreatedAtUtc,
            [.. sale.Lines.Select(l => new BarReceiptLine(l.Name, l.Quantity, l.UnitPrice, l.LineTotal))]));
    }
}

public static class ConcessionEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var bar = app.MapGroup("/api/backoffice/bar").WithTags("Back office");

        // The till needs the menu; only the manager changes it.
        bar.MapGet("/items", async (CinemaDbContext db, CancellationToken ct) =>
                Results.Ok((await db.ConcessionItems.AsNoTracking()
                    .OrderBy(i => i.Category).ThenBy(i => i.Name).ToListAsync(ct)).Select(i => i.ToDto())))
            .RequireAuthorization(AppPolicies.BackOffice).WithName("GetBarItems");

        bar.MapPost("/sell", async Task<Results<Ok<BarSaleResult>, Conflict<Error>, BadRequest<Error>>> (
                BarSaleCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "conflict" ? TypedResults.Conflict(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .RequireAuthorization(AppPolicies.BackOffice).WithName("SellAtBar");

        var menu = bar.MapGroup("/items").RequireAuthorization(AppRoles.Admin);

        menu.MapPost("", async Task<Results<Ok<ConcessionItemDto>, BadRequest<Error>, NotFound<Error>>> (
                SaveConcessionItemCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = null }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("CreateBarItem");

        menu.MapPut("/{id:guid}", async Task<Results<Ok<ConcessionItemDto>, BadRequest<Error>, NotFound<Error>>> (
                Guid id, SaveConcessionItemCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("UpdateBarItem");

        menu.MapPost("/{id:guid}/restock", async Task<Results<Ok<ConcessionItemDto>, BadRequest<Error>, NotFound<Error>>> (
                Guid id, RestockCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("RestockBarItem");

        menu.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, CinemaDbContext db, CancellationToken ct) =>
            {
                var item = await db.ConcessionItems.FirstOrDefaultAsync(i => i.Id == id, ct);
                if (item is null) return TypedResults.NotFound();
                db.ConcessionItems.Remove(item);   // soft delete; old receipts keep their snapshot
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteBarItem");
    }
}
