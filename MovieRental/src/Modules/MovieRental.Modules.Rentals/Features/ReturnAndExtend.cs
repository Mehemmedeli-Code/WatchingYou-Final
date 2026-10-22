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

// Feature 3 (continued) — extend the due date, then return the copy.
public sealed record ExtendRentalCommand(Guid RentalId, int ExtraDays) : ICommand<Result<RentalResponse>>;

internal sealed class ExtendRentalHandler(RentalsDbContext db, ICurrentUser currentUser, LateFeePolicy policy)
    : ICommandHandler<ExtendRentalCommand, Result<RentalResponse>>
{
    private const int MaxExtensions = 2;

    public async Task<Result<RentalResponse>> Handle(ExtendRentalCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == command.RentalId && r.UserId == userId, ct);

        if (rental is null) return Result.Failure<RentalResponse>(Error.NotFound("Rental"));
        if (rental.ReturnedAtUtc is not null)
            return Result.Failure<RentalResponse>(Error.Conflict("This rental is already closed."));
        if (rental.ExtensionCount >= MaxExtensions)
            return Result.Failure<RentalResponse>(Error.Conflict($"A rental can be extended {MaxExtensions} times."));
        if (command.ExtraDays is < 1 or > 14)
            return Result.Failure<RentalResponse>(Error.Validation("Extend by 1 to 14 days."));

        // Extending from the current due date, not from today, so a late customer cannot
        // extend their way out of a fee they have already earned.
        rental.DueAtUtc = rental.DueAtUtc.AddDays(command.ExtraDays);
        rental.BasePrice = Math.Round(rental.BasePrice + rental.DailyPrice * command.ExtraDays, 2);
        rental.ExtensionCount++;
        rental.DueSoonNotified = false;

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
            async Task<Results<Ok<RentalResponse>, Conflict<Error>, NotFound<Error>>> (
                Guid id, ExtendRentalCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { RentalId = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Conflict(result.Error);
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
