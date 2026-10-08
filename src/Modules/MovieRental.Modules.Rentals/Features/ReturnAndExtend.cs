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
public sealed record ExtendRentalCommand(Guid RentalId, int ExtraDays = RentalPricing.PeriodDays) : ICommand<Result<RentalResponse>>;

internal sealed class ExtendRentalHandler(RentalsDbContext db, ICurrentUser currentUser, LateFeePolicy policy)
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

        // Counted from the moment they say yes. The time they took to decide was free and
        // does not eat into the days they are now paying for.
        rental.DueAtUtc = now.AddDays(RentalPricing.PeriodDays);
        rental.BasePrice = Math.Round(rental.BasePrice + RentalPricing.PeriodPrice, 2);
        rental.ExtensionCount++;
        rental.DueSoonNotified = false;
        rental.OverdueNotified = false;   // the next "keep or return?" gets its own e-mail

        await db.SaveChangesAsync(ct);
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
        rental.ReturnedAtUtc = now;
        rental.LateFee = policy.Calculate(rental, now);

        await db.SaveChangesAsync(ct);
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
