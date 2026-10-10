using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Rentals.Domain;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Rentals.Features;

// Feature 3 (continued) — when the paid three days are over: three more for $0.50, or return.
// ExtraDays is kept in the request for older clients and ignored; a period is always three days.
public sealed record ExtendRentalCommand(Guid RentalId, int ExtraDays = RentalPricing.PeriodDays, ProCard? Card = null) : ICommand<Result<RentalResponse>>;

internal sealed class ExtendRentalHandler(RentalsDbContext db, ICurrentUser currentUser, LateFeePolicy policy,
    Microsoft.Extensions.Configuration.IConfiguration configuration)
    : ICommandHandler<ExtendRentalCommand, Result<RentalResponse>>
{
    public async Task<Result<RentalResponse>> Handle(ExtendRentalCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == command.RentalId && r.UserId == userId, ct);

        if (rental is null) return Result.Failure<RentalResponse>(Error.NotFound("Rental"));
        if (rental.ReturnedAtUtc is not null)
            return Result.Failure<RentalResponse>(Error.Conflict("This rental is already closed."));
        // The offer appears once the paid days are over, not before: paying for days you
        // already have would be paying twice.
        var now = DateTime.UtcNow;
        if (now < rental.DueAtUtc)
            return Result.Failure<RentalResponse>(Error.Conflict("You can add three more days once the current three are over."));

        var paid = CardPayment.Check(command.Card, configuration);
        if (paid.IsFailure) return Result.Failure<RentalResponse>(paid.Error);

        // Counted from the moment they say yes. The time they took to decide was free and
        // does not eat into the days they are now paying for.
        // Conditional on the due date still being the one read above: of two requests at once
        // (a double tap) only one extends and is charged; the other is told it is done.
        var due = rental.DueAtUtc;
        var newDue = now.AddDays(RentalPricing.PeriodDays);
        var extended = await db.Rentals
            .Where(r => r.Id == rental.Id && r.ReturnedAtUtc == null && r.DueAtUtc == due)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DueAtUtc, newDue)
                .SetProperty(r => r.BasePrice, r => r.BasePrice + RentalPricing.PeriodPrice)
                .SetProperty(r => r.ExtensionCount, r => r.ExtensionCount + 1)
                .SetProperty(r => r.DueSoonNotified, false)
                .SetProperty(r => r.OverdueNotified, false), ct);
        if (extended == 0)
            return Result.Failure<RentalResponse>(Error.Conflict("You can add three more days once the current three are over."));

        await db.Entry(rental).ReloadAsync(ct);
        return Result.Success(rental.ToResponse(policy, DateTime.UtcNow));
    }
}

public sealed record ReturnRentalCommand(Guid RentalId) : ICommand<Result<RentalResponse>>;

internal sealed class ReturnRentalHandler(
    RentalsDbContext db, ICatalogApi catalog, ICurrentUser currentUser, LateFeePolicy policy)
    : ICommandHandler<ReturnRentalCommand, Result<RentalResponse>>
{
    public async Task<Result<RentalResponse>> Handle(ReturnRentalCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == command.RentalId && r.UserId == userId, ct);

        if (rental is null) return Result.Failure<RentalResponse>(Error.NotFound("Rental"));
        if (rental.ReturnedAtUtc is not null)
            return Result.Failure<RentalResponse>(Error.Conflict("This rental was already returned."));

        var now = DateTime.UtcNow;
        var fee = policy.Calculate(rental, now);

        // One conditional UPDATE decides who returns it. Read-then-save let several requests at
        // once all see "not returned" and each put a copy back on the shelf — copies that were
        // still out with other customers.
        var closed = await db.Rentals
            .Where(r => r.Id == rental.Id && r.ReturnedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReturnedAtUtc, now).SetProperty(r => r.LateFee, fee), ct);
        if (closed == 0)
            return Result.Failure<RentalResponse>(Error.Conflict("This rental was already returned."));

        rental.ReturnedAtUtc = now;
        rental.LateFee = fee;
        await catalog.ReleaseCopyAsync(rental.MovieId, ct);

        return Result.Success(rental.ToResponse(policy, now));
    }
}

public static class ReturnAndExtendEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/rentals/{id:guid}/extend",
            async Task<Results<Ok<RentalResponse>, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, ExtendRentalCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { RentalId = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                // A bad number of days is the caller's input (400); a closed or maxed-out rental
                // is the state of things (409). Both used to come back as 409.
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "validation" => TypedResults.BadRequest(result.Error),
                    _ => TypedResults.Conflict(result.Error)
                };
            })
        .WithName("PutRentalExtensionWithId").WithTags("Rentals").RequireAuthorization();

        app.MapPut("/api/rentals/{id:guid}/return",
            async Task<Results<Ok<RentalResponse>, Conflict<Error>, NotFound<Error>>> (
                Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new ReturnRentalCommand(id), ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Conflict(result.Error);
            })
        .WithName("ReturnRentalWithId").WithTags("Rentals").RequireAuthorization();
    }
}
