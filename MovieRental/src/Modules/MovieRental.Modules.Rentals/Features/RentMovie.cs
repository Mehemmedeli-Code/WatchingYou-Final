using FluentValidation;
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

// Feature 3 — one-click rental.
public sealed record RentMovieCommand(Guid MovieId, int Days) : ICommand<Result<RentalResponse>>;

internal sealed class RentMovieValidator : AbstractValidator<RentMovieCommand>
{
    public RentMovieValidator()
    {
        RuleFor(x => x.MovieId).NotEmpty();
        RuleFor(x => x.Days).InclusiveBetween(1, 30);
    }
}

internal sealed class RentMovieHandler(
    RentalsDbContext db, ICatalogApi catalog, ICurrentUser currentUser, IEmailSender email, IUserDirectory users)
    : ICommandHandler<RentMovieCommand, Result<RentalResponse>>
{
    public async Task<Result<RentalResponse>> Handle(RentMovieCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        var alreadyOut = await db.Rentals.AnyAsync(
            r => r.UserId == userId && r.MovieId == command.MovieId && r.ReturnedAtUtc == null, ct);
        if (alreadyOut)
            return Result.Failure<RentalResponse>(Error.Conflict("You already have this title out on rental."));

        var movie = await catalog.GetMovieAsync(command.MovieId, ct);
        if (movie is null) return Result.Failure<RentalResponse>(Error.NotFound("Movie"));

        // Stock is taken first. If writing the rental row then fails, the compensating
        // release below puts the copy back — the two modules write to separate schemas,
        // so a single ambient transaction would need MSDTC and buy little here.
        if (!await catalog.TryReserveCopyAsync(command.MovieId, ct))
            return Result.Failure<RentalResponse>(Error.Conflict("Every copy is currently out. Try again later."));

        var rental = new Rental
        {
            UserId = userId,
            MovieId = movie.Id,
            MovieTitle = movie.Title,
            PosterUrl = movie.PosterUrl,
            RentedAtUtc = DateTime.UtcNow,
            DueAtUtc = DateTime.UtcNow.AddDays(command.Days),
            DailyPrice = movie.DailyPrice,
            BasePrice = Math.Round(movie.DailyPrice * command.Days, 2)
        };

        try
        {
            db.Rentals.Add(rental);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await catalog.ReleaseCopyAsync(command.MovieId, CancellationToken.None);
            throw;
        }

        var contact = await users.GetContactAsync(userId, ct);
        if (contact is not null)
            await email.SendAsync(new EmailRequest(contact.Email, $"You rented {movie.Title}",
                $"<p>Enjoy the film. Please return it by <strong>{rental.DueAtUtc:dd MMM yyyy}</strong>.</p>"), ct);

        return Result.Success(rental.ToResponse(new LateFeePolicy(), DateTime.UtcNow));
    }
}

public static class RentMovieEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/rentals",
            async Task<Results<Ok<RentalResponse>, Conflict<Error>, NotFound<Error>>> (
                RentMovieCommand command, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(command, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Conflict(result.Error);
            })
        .WithName("RentMovie").WithTags("Rentals").RequireAuthorization();
}

internal static class RentalMapper
{
    public static RentalResponse ToResponse(this Rental r, LateFeePolicy policy, DateTime nowUtc)
    {
        var fee = r.ReturnedAtUtc is null ? policy.Calculate(r, nowUtc) : r.LateFee;
        return new RentalResponse(
            r.Id, r.MovieId, r.MovieTitle, r.PosterUrl, r.RentedAtUtc, r.DueAtUtc, r.ReturnedAtUtc,
            r.DailyPrice, r.BasePrice, fee, Math.Round(r.BasePrice + fee, 2),
            r.DaysOverdue(nowUtc), r.ExtensionCount, r.StatusAt(nowUtc));
    }
}
