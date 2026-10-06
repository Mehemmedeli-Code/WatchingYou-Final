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

// Manager set-up: the price bands sold at the box office, and the terms each film is played on.

// ------------------------------------------------------------------ ticket types

/// <param name="Name">The stable English name.</param>
/// <param name="NameAz">Shown in Azerbaijani; null falls back to Name. Likewise Ru and Tr.</param>
public sealed record TicketTypeDto(
    Guid Id, string Name, decimal PercentOfBase, int SortOrder, bool IsActive,
    string? NameAz = null, string? NameRu = null, string? NameTr = null)
{
    public static TicketTypeDto From(TicketType t) =>
        new(t.Id, t.Name, t.PercentOfBase, t.SortOrder, t.IsActive, t.NameAz, t.NameRu, t.NameTr);
}

public sealed record SaveTicketTypeCommand(
    Guid? Id, string Name, decimal PercentOfBase, int SortOrder, bool IsActive,
    string? NameAz = null, string? NameRu = null, string? NameTr = null)
    : ICommand<Result<TicketTypeDto>>;

internal sealed class SaveTicketTypeValidator : AbstractValidator<SaveTicketTypeCommand>
{
    public SaveTicketTypeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(40);
        RuleFor(x => x.NameAz).MaximumLength(40);
        RuleFor(x => x.NameRu).MaximumLength(40);
        RuleFor(x => x.NameTr).MaximumLength(40);
        // Up to 300%: a VIP or 3D surcharge is a type too.
        RuleFor(x => x.PercentOfBase).InclusiveBetween(0, 300);
    }
}

internal sealed class SaveTicketTypeHandler(CinemaDbContext db) : ICommandHandler<SaveTicketTypeCommand, Result<TicketTypeDto>>
{
    public async Task<Result<TicketTypeDto>> Handle(SaveTicketTypeCommand command, CancellationToken ct)
    {
        var type = command.Id is { } id ? await db.TicketTypes.FirstOrDefaultAsync(t => t.Id == id, ct) : null;
        if (command.Id is not null && type is null) return Result.Failure<TicketTypeDto>(Error.NotFound("Ticket type"));

        if (type is null)
        {
            type = new TicketType { Name = command.Name.Trim() };
            db.TicketTypes.Add(type);
        }

        type.Name = command.Name.Trim();
        type.NameAz = Clean(command.NameAz);
        type.NameRu = Clean(command.NameRu);
        type.NameTr = Clean(command.NameTr);
        type.PercentOfBase = command.PercentOfBase;
        type.SortOrder = command.SortOrder;
        type.IsActive = command.IsActive;

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result.Failure<TicketTypeDto>(Error.Conflict($"A ticket type called \"{type.Name}\" already exists."));
        }

        return Result.Success(TicketTypeDto.From(type));
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

// ------------------------------------------------------------------ distributor deals

public sealed record FilmDealDto(Guid Id, Guid MovieId, string MovieTitle, string Distributor, decimal SharePercent, string? Note);

public sealed record SaveFilmDealCommand(Guid MovieId, string Distributor, decimal SharePercent, string? Note)
    : ICommand<Result<FilmDealDto>>;

internal sealed class SaveFilmDealValidator : AbstractValidator<SaveFilmDealCommand>
{
    public SaveFilmDealValidator()
    {
        RuleFor(x => x.MovieId).NotEmpty();
        RuleFor(x => x.Distributor).NotEmpty().MaximumLength(120);
        RuleFor(x => x.SharePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

/// <summary>One deal per film: saving again replaces the terms rather than adding a second
/// row that the settlement report would have to choose between.</summary>
internal sealed class SaveFilmDealHandler(CinemaDbContext db, ICatalogApi catalog, IAuditLog audit)
    : ICommandHandler<SaveFilmDealCommand, Result<FilmDealDto>>
{
    public async Task<Result<FilmDealDto>> Handle(SaveFilmDealCommand command, CancellationToken ct)
    {
        var movie = await catalog.GetMovieAsync(command.MovieId, ct);
        if (movie is null) return Result.Failure<FilmDealDto>(Error.NotFound("Film"));

        var deal = await db.FilmDeals.FirstOrDefaultAsync(d => d.MovieId == movie.Id, ct);
        if (deal is null)
        {
            deal = new FilmDeal { MovieId = movie.Id, MovieTitle = movie.Title, Distributor = command.Distributor.Trim() };
            db.FilmDeals.Add(deal);
        }

        deal.MovieTitle = movie.Title;
        deal.Distributor = command.Distributor.Trim();
        deal.SharePercent = command.SharePercent;
        deal.Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry("deal.saved", movie.Title, $"{deal.Distributor} · {deal.SharePercent:0.##}%", deal.Id), ct);
        return Result.Success(new FilmDealDto(deal.Id, deal.MovieId, deal.MovieTitle, deal.Distributor, deal.SharePercent, deal.Note));
    }
}

public static class BackOfficeSetupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // The till reads the active types; everything else is the manager's.
        app.MapGet("/api/backoffice/ticket-types", async (bool? all, CinemaDbContext db, ICurrentUser user, CancellationToken ct) =>
            {
                var includeInactive = all == true && user.IsInRole(AppRoles.Admin);
                return Results.Ok(await db.TicketTypes.AsNoTracking()
                    .Where(t => includeInactive || t.IsActive)
                    .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                    .Select(t => new TicketTypeDto(t.Id, t.Name, t.PercentOfBase, t.SortOrder, t.IsActive, t.NameAz, t.NameRu, t.NameTr))
                    .ToListAsync(ct));
            })
            .WithTags("Back office").RequireAuthorization(AppPolicies.BackOffice).WithName("GetTicketTypes");

        var types = app.MapGroup("/api/backoffice/ticket-types").WithTags("Back office").RequireAuthorization(AppRoles.Admin);

        types.MapPost("", async Task<Results<Ok<TicketTypeDto>, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                SaveTicketTypeCommand body, IDispatcher dispatcher, CancellationToken ct) =>
                Map(await dispatcher.Send(body with { Id = null }, ct)))
            .WithName("CreateTicketType");

        types.MapPut("/{id:guid}", async Task<Results<Ok<TicketTypeDto>, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, SaveTicketTypeCommand body, IDispatcher dispatcher, CancellationToken ct) =>
                Map(await dispatcher.Send(body with { Id = id }, ct)))
            .WithName("UpdateTicketType");

        types.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, CinemaDbContext db, CancellationToken ct) =>
            {
                var type = await db.TicketTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (type is null) return TypedResults.NotFound();
                db.TicketTypes.Remove(type);
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteTicketType");

        var deals = app.MapGroup("/api/backoffice/deals").WithTags("Back office").RequireAuthorization(AppRoles.Admin);

        deals.MapGet("", async (CinemaDbContext db, CancellationToken ct) =>
                Results.Ok(await db.FilmDeals.AsNoTracking().OrderBy(d => d.MovieTitle)
                    .Select(d => new FilmDealDto(d.Id, d.MovieId, d.MovieTitle, d.Distributor, d.SharePercent, d.Note))
                    .ToListAsync(ct)))
            .WithName("GetFilmDeals");

        deals.MapPost("", async Task<Results<Ok<FilmDealDto>, BadRequest<Error>, NotFound<Error>>> (
                SaveFilmDealCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.BadRequest(result.Error);
            })
            .WithName("SaveFilmDeal");

        deals.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, CinemaDbContext db, CancellationToken ct) =>
            {
                var deal = await db.FilmDeals.FirstOrDefaultAsync(d => d.Id == id, ct);
                if (deal is null) return TypedResults.NotFound();
                db.FilmDeals.Remove(deal);
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteFilmDeal");
    }

    private static Results<Ok<TicketTypeDto>, BadRequest<Error>, Conflict<Error>, NotFound<Error>> Map(Result<TicketTypeDto> result)
    {
        if (result.IsSuccess) return TypedResults.Ok(result.Value);
        return result.Error.Code switch
        {
            "not_found" => TypedResults.NotFound(result.Error),
            "conflict" => TypedResults.Conflict(result.Error),
            _ => TypedResults.BadRequest(result.Error)
        };
    }
}
