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

// Screenings used to appear only from the development seeder, which made Movies on Display
// impossible to curate. This is the admin-side slice that fixes that.

public sealed record ScreeningAdminItem(
    Guid Id, Guid MovieId, string MovieTitle, Guid HallId, string VenueName, string Hall,
    DateTime StartsAtUtc, int Rows, int SeatsPerRow, decimal SeatPrice,
    string AudioLanguage, string? SubtitleLanguage, int SeatsSold,
    bool IsCancelled, string? CancellationReason,
    int? RefundWindowHours, decimal? RefundFeePercent, string? RefundNote);

public sealed record SaveScreeningCommand(
    Guid? Id, Guid MovieId, Guid HallId, DateTime StartsAtUtc,
    decimal SeatPrice, string AudioLanguage, string? SubtitleLanguage,
    int? RefundWindowHours, decimal? RefundFeePercent, string? RefundNote)
    : ICommand<Result<ScreeningAdminItem>>;

public sealed record CancelScreeningCommand(Guid Id, string Reason) : ICommand<Result>;

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
        RuleFor(x => x.SeatPrice).GreaterThan(0);
        RuleFor(x => x.AudioLanguage).Must(l => Languages.Contains(l))
            .WithMessage("Audio language must be one of az, en, ru, tr.");
        RuleFor(x => x.SubtitleLanguage).Must(l => l is null || Languages.Contains(l))
            .WithMessage("Subtitle language must be one of az, en, ru, tr.");
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

        var screening = command.Id is { } id
            ? await db.Screenings.Include(s => s.Bookings).FirstOrDefaultAsync(s => s.Id == id, ct)
            : null;

        if (command.Id is not null && screening is null)
            return Result.Failure<ScreeningAdminItem>(Error.NotFound("Screening"));

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
        // Snapshotted, so re-fitting the room later never invalidates seats already sold.
        screening.Rows = hall.Rows;
        screening.SeatsPerRow = hall.SeatsPerRow;
        screening.SeatPrice = command.SeatPrice;
        screening.AudioLanguage = command.AudioLanguage;
        screening.SubtitleLanguage = command.SubtitleLanguage;
        screening.RefundWindowHours = command.RefundWindowHours;
        screening.RefundFeePercent = command.RefundFeePercent;
        screening.RefundNote = string.IsNullOrWhiteSpace(command.RefundNote) ? null : command.RefundNote.Trim();

        await db.SaveChangesAsync(ct);

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

public static class ManageScreeningsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/screenings").WithTags("Cinema admin")
            .RequireAuthorization(AppRoles.Admin);

        admin.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetScreeningsForAdminQuery(), ct)))
            .WithName("GetScreeningsForAdmin");

        admin.MapPost("", async Task<Results<Ok<ScreeningAdminItem>, BadRequest<Error>, NotFound<Error>>> (
            SaveScreeningCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body with { Id = null }, ct);
            if (result.IsSuccess) return TypedResults.Ok(result.Value);
            return result.Error.Code == "not_found"
                ? TypedResults.NotFound(result.Error)
                : TypedResults.BadRequest(result.Error);
        }).WithName("CreateScreening");

        admin.MapPut("/{id:guid}", async Task<Results<Ok<ScreeningAdminItem>, BadRequest<Error>, NotFound<Error>>> (
            Guid id, SaveScreeningCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body with { Id = id }, ct);
            if (result.IsSuccess) return TypedResults.Ok(result.Value);
            return result.Error.Code == "not_found"
                ? TypedResults.NotFound(result.Error)
                : TypedResults.BadRequest(result.Error);
        }).WithName("UpdateScreeningWithId");

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
            }).WithName("CancelScreeningWithId");

        admin.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id, CinemaDbContext db, CancellationToken ct) =>
        {
            var screening = await db.Screenings.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (screening is null) return TypedResults.NotFound();

            db.Screenings.Remove(screening);   // soft delete, so sold seats keep their context
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }).WithName("SoftDeleteScreeningWithId");
    }
}
