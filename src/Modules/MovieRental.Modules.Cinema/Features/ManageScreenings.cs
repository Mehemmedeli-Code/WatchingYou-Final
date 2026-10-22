using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Cinema.Features;

// Screenings used to appear only from the development seeder, which made Movies on Display
// impossible to curate. This is the admin-side slice that fixes that.

public sealed record ScreeningAdminItem(
    Guid Id, Guid MovieId, string MovieTitle, Guid HallId, string VenueName, string Hall,
    DateTime StartsAtUtc, int Rows, int SeatsPerRow, decimal SeatPrice,
    string AudioLanguage, string? SubtitleLanguage, int SeatsSold,
    bool IsCancelled, string? CancellationReason,
    int? RefundWindowHours, decimal? RefundFeePercent, string? RefundNote);

/// <param name="Id">From the address when editing; not read from the body.</param>
public sealed record SaveScreeningCommand(
    [property: JsonIgnore] Guid? Id, Guid MovieId, Guid HallId, DateTime StartsAtUtc,
    decimal SeatPrice, string AudioLanguage, string? SubtitleLanguage,
    int? RefundWindowHours, decimal? RefundFeePercent, string? RefundNote)
    : ICommand<Result<ScreeningAdminItem>>;

public sealed record CancelScreeningCommand([property: JsonIgnore] Guid Id, string Reason) : ICommand<Result>;

internal sealed class CancelScreeningValidator : AbstractValidator<CancelScreeningCommand>
{
    public CancelScreeningValidator() => RuleFor(x => x.Reason).MaximumLength(300);
}

internal sealed class CancelScreeningHandler(CinemaDbContext db, IAuditLog audit)
    : ICommandHandler<CancelScreeningCommand, Result>
{
    public async Task<Result> Handle(CancelScreeningCommand command, CancellationToken ct)
    {
        var screening = await db.Screenings.FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (screening is null) return Result.Failure(Error.NotFound("Screening"));
        if (screening.IsCancelled) return Result.Failure(Error.Conflict("This screening is already cancelled."));
        if (string.IsNullOrWhiteSpace(command.Reason))
            return Result.Failure(Error.Validation("Say why it was cancelled — the customers will be told."));

        screening.IsCancelled = true;
        screening.CancellationReason = command.Reason.Trim();
        await db.SaveChangesAsync(ct);

        // The seats are deliberately left alone. Cancelling makes every booking fully
        // refundable, but the money goes back when the customer asks, not silently — and they
        // keep a record of what they held until then.
        await audit.RecordAsync(new AuditEntry(
            "screening.cancelled", $"{screening.MovieTitle} · {screening.Hall}", command.Reason, screening.Id), ct);

        return Result.Success();
    }
}

internal sealed class SaveScreeningValidator : AbstractValidator<SaveScreeningCommand>
{
    private static readonly string[] Languages = ["az", "en", "ru", "tr"];

    public SaveScreeningValidator()
    {
        RuleFor(x => x.MovieId).NotEmpty();
        RuleFor(x => x.HallId).NotEmpty();
        // Year 1 and year 9999 used to be saved, and every later read that moved them by a few
        // hours fell off the calendar.
        RuleFor(x => x.StartsAtUtc).Must(t => t.Year >= 2000 && t < DateTime.UtcNow.AddYears(2))
            .WithMessage("Screenings are scheduled up to two years ahead.");
        // A price or note too big for its column used to come back from the database as a 409.
        RuleFor(x => x.SeatPrice).GreaterThan(0).LessThan(1000);
        RuleFor(x => x.RefundWindowHours).InclusiveBetween(0, 24 * 30).When(x => x.RefundWindowHours is not null);
        RuleFor(x => x.RefundFeePercent).InclusiveBetween(0, 100).When(x => x.RefundFeePercent is not null);
        RuleFor(x => x.RefundNote).MaximumLength(500);
        RuleFor(x => x.AudioLanguage).Must(l => Languages.Contains(l))
            .WithMessage("Audio language must be one of az, en, ru, tr.");
        RuleFor(x => x.SubtitleLanguage).Must(l => l is null || Languages.Contains(l))
            .WithMessage("Subtitle language must be one of az, en, ru, tr.");
    }
}

/// <summary>
/// One writer per hall at a time. "Is the hall free then?" and the insert have to be one step:
/// ten saves sent together all read "free", and three shows landed in one room at the same time.
/// The same pattern as one PRO purchase per person (WatchingPro).
/// </summary>
internal static class HallLock
{
    public static async Task<IDbContextTransaction> TakeAsync(CinemaDbContext db, Guid hallId, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        var name = $"hall:{hallId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC sp_getapplock @Resource = {name}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000", ct);
        return tx;
    }
}

internal sealed class SaveScreeningHandler(CinemaDbContext db, ICatalogApi catalog)
    : ICommandHandler<SaveScreeningCommand, Result<ScreeningAdminItem>>
{
    public async Task<Result<ScreeningAdminItem>> Handle(SaveScreeningCommand command, CancellationToken ct)
    {
        // Seat layout is no longer typed in by hand: it comes from the room, which is also
        // what the 3D preview rebuilds. Two sources for one geometry would drift apart.
        var hall = await db.Halls.Include(h => h.Venue)
            .FirstOrDefaultAsync(h => h.Id == command.HallId, ct);
        if (hall is null) return Result.Failure<ScreeningAdminItem>(Error.NotFound("Hall"));

        // The title is asked for through the contract and then snapshotted, the same way
        // rentals do it: a screening in the past should still read correctly after a rename.
        var movie = await catalog.GetMovieAsync(command.MovieId, ct);
        if (movie is null) return Result.Failure<ScreeningAdminItem>(Error.NotFound("Film"));

        await using var tx = await HallLock.TakeAsync(db, hall.Id, ct);

        var screening = command.Id is { } id
            ? await db.Screenings.Include(s => s.Bookings).FirstOrDefaultAsync(s => s.Id == id, ct)
            : null;

        if (command.Id is not null && screening is null)
            return Result.Failure<ScreeningAdminItem>(Error.NotFound("Screening"));

        // The same two rules as scheduling in bulk: no show in the past (nobody could buy a seat)
        // and no two in one room at once. Only when the time or the room changes, so an old show
        // can still have its note or price corrected.
        if (screening is null || screening.StartsAtUtc != command.StartsAtUtc || screening.HallId != hall.Id)
        {
            if (command.StartsAtUtc <= DateTime.UtcNow)
                return Result.Failure<ScreeningAdminItem>(Error.Validation("Pick a start time in the future."));
            var busy = await db.Screenings.AsNoTracking().AnyAsync(s =>
                s.HallId == hall.Id && !s.IsCancelled && s.Id != command.Id
                && s.StartsAtUtc > command.StartsAtUtc - BulkScheduleHandler.MinimumGap
                && s.StartsAtUtc < command.StartsAtUtc + BulkScheduleHandler.MinimumGap, ct);
            if (busy)
                return Result.Failure<ScreeningAdminItem>(Error.Conflict(
                    $"{hall.Name} is busy then: another screening starts less than 2½ hours before or after."));
        }

        if (screening is null)
        {
            screening = new Screening { MovieId = movie.Id, MovieTitle = movie.Title, Hall = hall.Name };
            db.Screenings.Add(screening);
        }
        else if (screening.Bookings.Count > 0 && screening.HallId != hall.Id)
        {
            // Moving a screening to a differently shaped room would orphan sold seats.
            return Result.Failure<ScreeningAdminItem>(
                Error.Conflict("Seats are already sold for this screening, so the hall is fixed."));
        }

        screening.MovieId = movie.Id;
        screening.MovieTitle = movie.Title;
        screening.HallId = hall.Id;
        screening.Hall = hall.Name;
        screening.StartsAtUtc = command.StartsAtUtc;
        // Snapshotted, so re-fitting the room later never invalidates seats already sold. Only
        // taken from the hall while nothing is sold: re-copying on every edit undid the snapshot.
        if (screening.Bookings.Count == 0)
        {
            screening.Rows = hall.Rows;
            screening.SeatsPerRow = hall.SeatsPerRow;
        }
        screening.SeatPrice = command.SeatPrice;
        screening.AudioLanguage = command.AudioLanguage;
        screening.SubtitleLanguage = command.SubtitleLanguage;
        screening.RefundWindowHours = command.RefundWindowHours;
        screening.RefundFeePercent = command.RefundFeePercent;
        screening.RefundNote = string.IsNullOrWhiteSpace(command.RefundNote) ? null : command.RefundNote.Trim();

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result.Success(new ScreeningAdminItem(
            screening.Id, screening.MovieId, screening.MovieTitle, hall.Id, hall.Venue?.Name ?? "",
            hall.Name, screening.StartsAtUtc, screening.Rows, screening.SeatsPerRow, screening.SeatPrice,
            screening.AudioLanguage, screening.SubtitleLanguage, screening.Bookings.Count,
            screening.IsCancelled, screening.CancellationReason,
            screening.RefundWindowHours, screening.RefundFeePercent, screening.RefundNote));
    }
}

public sealed record GetScreeningsForAdminQuery : IQuery<IReadOnlyList<ScreeningAdminItem>>;

internal sealed class GetScreeningsForAdminHandler(CinemaDbContext db)
    : IQueryHandler<GetScreeningsForAdminQuery, IReadOnlyList<ScreeningAdminItem>>
{
    public async Task<IReadOnlyList<ScreeningAdminItem>> Handle(GetScreeningsForAdminQuery query, CancellationToken ct) =>
        await db.Screenings.AsNoTracking()
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new ScreeningAdminItem(
                s.Id, s.MovieId, s.MovieTitle, s.HallId, s.HallRoom!.Venue!.Name, s.Hall,
                s.StartsAtUtc, s.Rows, s.SeatsPerRow, s.SeatPrice,
                s.AudioLanguage, s.SubtitleLanguage, s.Bookings.Count,
                s.IsCancelled, s.CancellationReason,
                s.RefundWindowHours, s.RefundFeePercent, s.RefundNote))
            .ToListAsync(ct);
}

public sealed record DeleteScreeningCommand(Guid Id) : ICommand<Result>;

internal sealed class DeleteScreeningHandler(CinemaDbContext db) : ICommandHandler<DeleteScreeningCommand, Result>
{
    public async Task<Result> Handle(DeleteScreeningCommand command, CancellationToken ct)
    {
        var screening = await db.Screenings.FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (screening is null) return Result.Failure(Error.NotFound("Screening"));

        // Payments are filtered out with their screening, so deleting one with seats held or
        // sold made those tickets vanish with no way to refund them. Cancel refunds them.
        if (await db.SeatBookings.AnyAsync(b => b.ScreeningId == command.Id, ct))
            return Result.Failure(Error.Conflict("Seats are held or sold for this screening. Cancel it instead, which refunds them."));

        db.Screenings.Remove(screening);   // soft delete, so sold seats keep their context
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class ManageScreeningsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/screenings").WithTags("Cinema admin")
            .RequireAuthorization(AppRoles.Admin);

        admin.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.Ask(new GetScreeningsForAdminQuery(), ct)))
            .WithName("GetScreeningsForAdmin")
            .WithSummary("Every screening, with seats sold and its refund rules");

        admin.MapPost("", async Task<Results<Ok<ScreeningAdminItem>, BadRequest<Error>, NotFound<Error>, Conflict<Error>>> (
            SaveScreeningCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            Answer(await dispatcher.Send(body with { Id = null }, ct)))
        .WithName("CreateScreening")
        .WithSummary("Schedule one screening")
        .WithDescription("`movieId`, `hallId`, `startsAtUtc` (in the future, up to two years ahead), `seatPrice` (above 0, below 1000), " +
                         "`audioLanguage` and optional `subtitleLanguage` (az, en, ru, tr). Optional refund rules: `refundWindowHours`, " +
                         "`refundFeePercent`, `refundNote`. 409: another screening in that hall starts less than 2½ hours before or after.");

        admin.MapPut("/{id:guid}", async Task<Results<Ok<ScreeningAdminItem>, BadRequest<Error>, NotFound<Error>, Conflict<Error>>> (
            Guid id, SaveScreeningCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            Answer(await dispatcher.Send(body with { Id = id }, ct)))
        .WithName("UpdateScreeningWithId")
        .WithSummary("Edit a screening")
        .WithDescription("The same fields as scheduling one; the time rules apply only when the time or the hall changes. " +
                         "409: the hall is busy then, or seats are already sold so the hall cannot change.");

        admin.MapPost("/{id:guid}/cancel",
            async Task<Results<NoContent, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, CancelScreeningCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            }).WithName("CancelScreeningWithId")
            .WithSummary("Cancel a screening (every booking becomes fully refundable)")
            .WithDescription("`reason` is shown to the customers (1-300 characters). 409: already cancelled.");

        admin.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound<Error>, Conflict<Error>>> (
            Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(new DeleteScreeningCommand(id), ct);
            if (result.IsSuccess) return TypedResults.NoContent();
            return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.Conflict(result.Error);
        }).WithName("SoftDeleteScreeningWithId")
        .WithSummary("Delete a screening nobody has booked")
        .WithDescription("409 once seats are held or sold: cancel it instead, which makes them refundable.");
    }

    private static Results<Ok<ScreeningAdminItem>, BadRequest<Error>, NotFound<Error>, Conflict<Error>> Answer(Result<ScreeningAdminItem> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.Code switch
        {
            "not_found" => TypedResults.NotFound(result.Error),
            "conflict" => TypedResults.Conflict(result.Error),
            _ => TypedResults.BadRequest(result.Error)
        };
}
