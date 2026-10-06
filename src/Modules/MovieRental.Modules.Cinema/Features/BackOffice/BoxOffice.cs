using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Infrastructure;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// The box office: a cashier sells seats to someone standing at the counter.
//
// It writes the same SeatPayment and SeatBooking rows as the website, so the seat map, the
// door scanner, the ticket PDF and the revenue charts all see counter sales without knowing
// they exist. What differs: no e-mailed code (the customer is right there), no customer
// account (walk-ins have none), and the sale is tied to the cashier's open shift.

/// <summary>Walk-in customers have no account. Their tickets belong to nobody, so they never
/// appear under anybody's "my tickets".</summary>
internal static class WalkIn
{
    public static readonly Guid CustomerId = Guid.Empty;
}

public sealed record BoxOfficeScreening(
    Guid Id, string MovieTitle, string VenueName, string Hall, string? Format, DateTime StartsAtUtc,
    decimal SeatPrice, int Capacity, int SeatsSold, string AudioLanguage, string? SubtitleLanguage, bool IsOnSale);

public sealed record GetBoxOfficeScreeningsQuery(DateOnly Day) : IQuery<IReadOnlyList<BoxOfficeScreening>>;

internal sealed class GetBoxOfficeScreeningsHandler(CinemaDbContext db)
    : IQueryHandler<GetBoxOfficeScreeningsQuery, IReadOnlyList<BoxOfficeScreening>>
{
    public async Task<IReadOnlyList<BoxOfficeScreening>> Handle(GetBoxOfficeScreeningsQuery query, CancellationToken ct)
    {
        var from = BackOfficeClock.StartUtc(query.Day);
        var to = BackOfficeClock.StartUtc(query.Day.AddDays(1));
        var saleCutoff = DateTime.UtcNow - BoxOfficeSaleHandler.LateSaleWindow;

        return await db.Screenings.AsNoTracking()
            .Where(s => s.StartsAtUtc >= from && s.StartsAtUtc < to && !s.IsCancelled)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new BoxOfficeScreening(
                s.Id, s.MovieTitle, s.HallRoom!.Venue!.Name, s.Hall, s.HallRoom.Format, s.StartsAtUtc,
                s.SeatPrice, s.Rows * s.SeatsPerRow, s.Bookings.Count(b => b.ConfirmedAtUtc != null),
                s.AudioLanguage, s.SubtitleLanguage, s.StartsAtUtc > saleCutoff))
            .ToListAsync(ct);
    }
}

public sealed record BoxOfficeSeat(int Row, int Number, Guid TicketTypeId);

public sealed record BoxOfficeSaleCommand(
    Guid ScreeningId, IReadOnlyList<BoxOfficeSeat> Seats, TenderType Tender, decimal? CashReceived)
    : ICommand<Result<BoxOfficeSaleResult>>;

public sealed record BoxOfficeSaleResult(
    Guid PaymentId, string Reference, decimal Total, decimal? Change, TicketResponse Ticket);

internal sealed class BoxOfficeSaleValidator : AbstractValidator<BoxOfficeSaleCommand>
{
    public BoxOfficeSaleValidator()
    {
        RuleFor(x => x.Seats).NotEmpty().WithMessage("Pick at least one seat.");
        RuleFor(x => x.Seats.Count).LessThanOrEqualTo(20).WithMessage("Twenty seats is the most in one sale.");
        RuleFor(x => x.Tender).IsInEnum();
        RuleFor(x => x.CashReceived).GreaterThanOrEqualTo(0).When(x => x.CashReceived is not null);
    }
}

internal sealed class BoxOfficeSaleHandler(CinemaDbContext db, ICurrentUser currentUser)
    : ICommandHandler<BoxOfficeSaleCommand, Result<BoxOfficeSaleResult>>
{
    /// <summary>Late-comers still buy at the counter during the adverts.</summary>
    public static readonly TimeSpan LateSaleWindow = TimeSpan.FromMinutes(20);

    public async Task<Result<BoxOfficeSaleResult>> Handle(BoxOfficeSaleCommand command, CancellationToken ct)
    {
        var shift = await Shifts.OpenForAsync(db, currentUser.RequireId(), ct);
        if (shift is null)
            return Result.Failure<BoxOfficeSaleResult>(Error.Conflict("Open a cash shift before selling."));

        var screening = await db.Screenings
            .Include(s => s.HallRoom!).ThenInclude(h => h.Venue)
            .FirstOrDefaultAsync(s => s.Id == command.ScreeningId, ct);
        if (screening is null) return Result.Failure<BoxOfficeSaleResult>(Error.NotFound("Screening"));
        if (screening.IsCancelled)
            return Result.Failure<BoxOfficeSaleResult>(Error.Conflict("This screening has been cancelled."));
        if (screening.StartsAtUtc + LateSaleWindow < DateTime.UtcNow)
            return Result.Failure<BoxOfficeSaleResult>(Error.Conflict("Sales for this screening have closed."));

        if (command.Seats.DistinctBy(s => (s.Row, s.Number)).Count() != command.Seats.Count)
            return Result.Failure<BoxOfficeSaleResult>(Error.Validation("The same seat is in the basket twice."));

        foreach (var seat in command.Seats)
        {
            if (seat.Row < 1 || seat.Row > screening.Rows || seat.Number < 1 || seat.Number > screening.SeatsPerRow)
                return Result.Failure<BoxOfficeSaleResult>(Error.Validation($"Seat {seat.Row}-{seat.Number} is not in this hall."));
        }

        var typeIds = command.Seats.Select(s => s.TicketTypeId).Distinct().ToList();
        var types = await db.TicketTypes.AsNoTracking()
            .Where(t => typeIds.Contains(t.Id) && t.IsActive)
            .ToDictionaryAsync(t => t.Id, ct);
        if (types.Count != typeIds.Count)
            return Result.Failure<BoxOfficeSaleResult>(Error.Validation("One of the ticket types is not on sale."));

        var seats = command.Seats.Select(seat =>
        {
            var type = types[seat.TicketTypeId];
            return new SeatBooking
            {
                ScreeningId = screening.Id,
                UserId = WalkIn.CustomerId,
                Row = seat.Row,
                Number = seat.Number,
                PricePaid = type.PriceFor(screening.SeatPrice),
                TicketType = type.Name,
                ConfirmedAtUtc = DateTime.UtcNow
            };
        }).ToList();

        var total = seats.Sum(s => s.PricePaid);
        decimal? change = null;
        if (command.Tender == TenderType.Cash)
        {
            var received = command.CashReceived ?? total;
            if (received < total)
                return Result.Failure<BoxOfficeSaleResult>(Error.Validation($"Cash received is short of {total:0.00}."));
            change = received - total;
        }

        // Online holds that ran out still sit in the unique index until swept; clear them so
        // the counter is not refused a seat nobody holds any more.
        await CheckoutHandler.ReleaseExpiredHoldsAsync(db, screening.Id, ct);

        var now = DateTime.UtcNow;
        var payment = new SeatPayment
        {
            ScreeningId = screening.Id,
            UserId = WalkIn.CustomerId,
            Reference = CheckoutHandler.NewReference(),
            Amount = total,
            Subtotal = total,
            Provider = PaymentProvider.BoxOffice,
            Tender = command.Tender,
            ShiftId = shift.Id,
            Brand = CardBrand.Unknown,
            Last4 = "",
            CardHolder = shift.CashierName,
            // No code is ever sent for a counter sale; these stay empty rather than holding a
            // hash of nothing that looks, to a later reader, like a real secret.
            CodeHash = "",
            Salt = "",
            Status = PaymentStatus.Confirmed,
            ConfirmedAtUtc = now,
            ExpiresAtUtc = now,
            Seats = seats
        };
        db.SeatPayments.Add(payment);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The same guarantee as online: the filtered unique index lets exactly one sale win.
            return Result.Failure<BoxOfficeSaleResult>(
                Error.Conflict("One of those seats was just sold. Refresh the map and pick again."));
        }

        payment.Screening = screening;
        return Result.Success(new BoxOfficeSaleResult(
            payment.Id, payment.Reference, total, change, TicketMapper.ToTicket(payment)));
    }
}

public sealed record BoxOfficeTicketPdfQuery(Guid PaymentId) : IQuery<(byte[] Pdf, string Reference)?>;

internal sealed class BoxOfficeTicketPdfHandler(CinemaDbContext db)
    : IQueryHandler<BoxOfficeTicketPdfQuery, (byte[] Pdf, string Reference)?>
{
    public async Task<(byte[] Pdf, string Reference)?> Handle(BoxOfficeTicketPdfQuery query, CancellationToken ct)
    {
        var payment = await db.SeatPayments.AsNoTracking()
            .Include(p => p.Seats)
            .Include(p => p.Screening!).ThenInclude(s => s.HallRoom!).ThenInclude(h => h.Venue)
            .FirstOrDefaultAsync(p => p.Id == query.PaymentId && p.Provider == PaymentProvider.BoxOffice
                                      && p.Status == PaymentStatus.Confirmed, ct);
        if (payment is null) return null;

        return (TicketPdf.Render(TicketMapper.ToTicket(payment), BookingFinalizer.VenueOf(payment)), payment.Reference);
    }
}

public static class BoxOfficeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var till = app.MapGroup("/api/backoffice/box-office").WithTags("Back office")
            .RequireAuthorization(AppPolicies.BackOffice);

        till.MapGet("/screenings", async (string? date, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(
                    new GetBoxOfficeScreeningsQuery(BackOfficeClock.Parse(date, BackOfficeClock.Today)), ct)))
            .WithName("GetBoxOfficeScreenings");

        till.MapPost("/sell", async Task<Results<Ok<BoxOfficeSaleResult>, NotFound<Error>, Conflict<Error>, BadRequest<Error>>> (
                BoxOfficeSaleCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            })
            .WithName("SellAtBoxOffice");

        till.MapGet("/{paymentId:guid}/ticket.pdf", async Task<Results<FileContentHttpResult, NotFound>> (
                Guid paymentId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var found = await dispatcher.Ask(new BoxOfficeTicketPdfQuery(paymentId), ct);
                return found is { } file
                    ? TypedResults.File(file.Pdf, "application/pdf", $"WatchingYou-{file.Reference}.pdf")
                    : TypedResults.NotFound();
            })
            .WithName("GetBoxOfficeTicketPdf");
    }
}
