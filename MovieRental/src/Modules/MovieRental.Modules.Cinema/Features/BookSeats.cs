using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

public sealed record SeatSelection(int Row, int Number);

public sealed record BookSeatsCommand(Guid ScreeningId, IReadOnlyList<SeatSelection> Seats)
    : ICommand<Result<BookingReceipt>>;

public sealed record BookingReceipt(Guid ScreeningId, string MovieTitle, IReadOnlyList<SeatSelection> Seats, decimal Total);

internal sealed class BookSeatsValidator : AbstractValidator<BookSeatsCommand>
{
    public BookSeatsValidator()
    {
        RuleFor(x => x.Seats).NotEmpty().WithMessage("Pick at least one seat.");
        RuleFor(x => x.Seats.Count).LessThanOrEqualTo(8).WithMessage("Eight seats is the limit per booking.");
    }
}

internal sealed class BookSeatsHandler(CinemaDbContext db, ICurrentUser currentUser)
    : ICommandHandler<BookSeatsCommand, Result<BookingReceipt>>
{
    public async Task<Result<BookingReceipt>> Handle(BookSeatsCommand command, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        var screening = await db.Screenings.Include(s => s.Bookings)
            .FirstOrDefaultAsync(s => s.Id == command.ScreeningId, ct);

        if (screening is null) return Result.Failure<BookingReceipt>(Error.NotFound("Screening"));
        if (screening.StartsAtUtc <= DateTime.UtcNow)
            return Result.Failure<BookingReceipt>(Error.Conflict("This screening has already started."));

        foreach (var seat in command.Seats)
        {
            if (seat.Row < 1 || seat.Row > screening.Rows || seat.Number < 1 || seat.Number > screening.SeatsPerRow)
                return Result.Failure<BookingReceipt>(Error.Validation($"Seat {seat.Row}-{seat.Number} is not in this hall."));

            if (screening.Bookings.Any(b => b.Row == seat.Row && b.Number == seat.Number))
                return Result.Failure<BookingReceipt>(Error.Conflict($"Seat {seat.Row}-{seat.Number} was just taken."));

            screening.Bookings.Add(new SeatBooking
            {
                ScreeningId = screening.Id,
                UserId = userId,
                Row = seat.Row,
                Number = seat.Number,
                PricePaid = screening.SeatPrice
            });
        }

        try
        {
            // All seats in one SaveChanges: a partly-booked row would leave the customer
            // paying for seats they cannot sit together in.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique index fired — someone else committed the same seat first.
            return Result.Failure<BookingReceipt>(Error.Conflict("One of those seats was booked a moment ago. Reload the map."));
        }

        var total = Math.Round(screening.SeatPrice * command.Seats.Count, 2);
        return Result.Success(new BookingReceipt(screening.Id, screening.MovieTitle, command.Seats, total));
    }
}

public static class BookSeatsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/screenings/{id:guid}/bookings",
            async Task<Results<Ok<BookingReceipt>, Conflict<Error>, NotFound<Error>>> (
                Guid id, BookSeatsCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { ScreeningId = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.Conflict(result.Error);
            })
        .WithName("BookSeatsWithId").WithTags("Cinema").RequireAuthorization();
}
