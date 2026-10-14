using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

// Feature 5 — admin inventory control: add, edit, soft-delete, restore, stock.

/// <summary>What creating and editing a film have in common, checked by one set of rules.</summary>
public interface IMovieFields
{
    string Title { get; }
    string Description { get; }
    string Genre { get; }
    int ReleaseYear { get; }
    int DurationMinutes { get; }
    string? Director { get; }
    string? PosterUrl { get; }
    string? TrailerUrl { get; }
    string? VideoUrl { get; }
    decimal DailyPrice { get; }
}

public sealed record CreateMovieCommand(
    string Title, string Description, string Genre, int ReleaseYear, int DurationMinutes,
    string? Director, string? PosterUrl, string? TrailerUrl, string? VideoUrl, decimal DailyPrice, int TotalCopies)
    : ICommand<Result<MovieListItem>>, IMovieFields;

// The edit had no rules at all: an empty body reached Title.Trim() and answered 500, and a
// 300-character title came back from the database as a "conflict".
internal sealed class MovieFieldsValidator : AbstractValidator<IMovieFields>
{
    public MovieFieldsValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Description).NotNull().MaximumLength(4000);
        RuleFor(x => x.Genre).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ReleaseYear).InclusiveBetween(1888, DateTime.UtcNow.Year + 5);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 600);
        RuleFor(x => x.Director).MaximumLength(150);
        RuleFor(x => x.PosterUrl).MaximumLength(500);
        RuleFor(x => x.TrailerUrl).MaximumLength(500);
        RuleFor(x => x.VideoUrl).MaximumLength(500);
        RuleFor(x => x.DailyPrice).GreaterThan(0).LessThan(1000);
    }
}

internal sealed class CreateMovieValidator : AbstractValidator<CreateMovieCommand>
{
    public CreateMovieValidator()
    {
        Include(new MovieFieldsValidator());
        RuleFor(x => x.TotalCopies).InclusiveBetween(0, 10_000);
    }
}

internal sealed class UpdateMovieValidator : AbstractValidator<UpdateMovieCommand>
{
    public UpdateMovieValidator() => Include(new MovieFieldsValidator());
}

internal sealed class CreateMovieHandler(CatalogDbContext db) : ICommandHandler<CreateMovieCommand, Result<MovieListItem>>
{
    public async Task<Result<MovieListItem>> Handle(CreateMovieCommand command, CancellationToken ct)
    {
        var slug = SlugFactory.Create(command.Title, command.ReleaseYear);
        if (await db.Movies.AnyAsync(m => m.Slug == slug, ct))
            return Result.Failure<MovieListItem>(Error.Conflict("That title and year are already in the catalogue."));

        var movie = new Movie
        {
            Title = command.Title.Trim(),
            Slug = slug,
            Description = command.Description.Trim(),
            Genre = command.Genre.Trim(),
            ReleaseYear = command.ReleaseYear,
            DurationMinutes = command.DurationMinutes,
            Director = command.Director?.Trim(),
            PosterUrl = command.PosterUrl?.Trim(),
            TrailerUrl = command.TrailerUrl?.Trim(),
            VideoUrl = command.VideoUrl?.Trim(),
            DailyPrice = command.DailyPrice,
            TotalCopies = command.TotalCopies,
            AvailableCopies = command.TotalCopies
        };

        db.Movies.Add(movie);
        await db.SaveChangesAsync(ct);
        return Result.Success(movie.ToListItem());
    }
}

/// <param name="Id">From the address; not read from the body.</param>
public sealed record UpdateMovieCommand(
    [property: JsonIgnore] Guid Id, string Title, string Description, string Genre, int ReleaseYear, int DurationMinutes,
    string? Director, string? PosterUrl, string? TrailerUrl, string? VideoUrl, decimal DailyPrice) : ICommand<Result>, IMovieFields;

internal sealed class UpdateMovieHandler(CatalogDbContext db) : ICommandHandler<UpdateMovieCommand, Result>
{
    public async Task<Result> Handle(UpdateMovieCommand command, CancellationToken ct)
    {
        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == command.Id, ct);
        if (movie is null) return Result.Failure(Error.NotFound("Movie"));

        var slug = SlugFactory.Create(command.Title, command.ReleaseYear);
        if (await db.Movies.AnyAsync(m => m.Slug == slug && m.Id != movie.Id, ct))
            return Result.Failure(Error.Conflict("That title and year are already in the catalogue."));

        movie.Title = command.Title.Trim();
        movie.Slug = slug;
        movie.Description = command.Description.Trim();
        movie.Genre = command.Genre.Trim();
        movie.ReleaseYear = command.ReleaseYear;
        movie.DurationMinutes = command.DurationMinutes;
        movie.Director = command.Director?.Trim();
        movie.PosterUrl = command.PosterUrl?.Trim();
        movie.TrailerUrl = command.TrailerUrl?.Trim();
        movie.VideoUrl = command.VideoUrl?.Trim();
        movie.DailyPrice = command.DailyPrice;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <param name="Id">From the address; not read from the body.</param>
public sealed record UpdateStockCommand([property: JsonIgnore] Guid Id, int TotalCopies) : ICommand<Result>;

internal sealed class UpdateStockValidator : AbstractValidator<UpdateStockCommand>
{
    // A negative stock is a typing mistake (400), not the "copies are out on rental" conflict
    // it used to be reported as.
    public UpdateStockValidator() => RuleFor(x => x.TotalCopies).InclusiveBetween(0, 10_000);
}

internal sealed class UpdateStockHandler(CatalogDbContext db) : ICommandHandler<UpdateStockCommand, Result>
{
    public async Task<Result> Handle(UpdateStockCommand command, CancellationToken ct)
    {
        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == command.Id, ct);
        if (movie is null) return Result.Failure(Error.NotFound("Movie"));

        var onLoan = movie.TotalCopies - movie.AvailableCopies;
        if (command.TotalCopies < onLoan)
            return Result.Failure(Error.Conflict($"{onLoan} copies are out on rental. Stock cannot go below that."));

        movie.TotalCopies = command.TotalCopies;
        movie.AvailableCopies = command.TotalCopies - onLoan;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SoftDeleteMovieCommand(Guid Id) : ICommand<Result>;

internal sealed class SoftDeleteMovieHandler(CatalogDbContext db) : ICommandHandler<SoftDeleteMovieCommand, Result>
{
    public async Task<Result> Handle(SoftDeleteMovieCommand command, CancellationToken ct)
    {
        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == command.Id, ct);
        if (movie is null) return Result.Failure(Error.NotFound("Movie"));

        // Remove() is intercepted by ModuleDbContext and rewritten into IsDeleted = true.
        db.Movies.Remove(movie);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record RestoreMovieCommand(Guid Id) : ICommand<Result>;

internal sealed class RestoreMovieHandler(CatalogDbContext db) : ICommandHandler<RestoreMovieCommand, Result>
{
    public async Task<Result> Handle(RestoreMovieCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters is the only way to reach a row the global filter hides.
        var movie = await db.Movies.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == command.Id, ct);
        if (movie is null) return Result.Failure(Error.NotFound("Movie"));

        movie.IsDeleted = false;
        movie.DeletedAtUtc = null;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public static class ManageMoviesEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/movies").WithTags("Catalog admin").RequireAuthorization(AppRoles.Admin);

        admin.MapPost("/", async Task<Results<Ok<MovieListItem>, Conflict<Error>>> (
            CreateMovieCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(command, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Conflict(result.Error);
        }).WithName("CreateMovie")
        .WithSummary("Add a film to the catalogue")
        .WithDescription("`releaseYear` 1888 to five years ahead, `durationMinutes` 1 to 600, `dailyPrice` above 0 and below 1000, " +
                         "`totalCopies` 0 to 10000. 409: a film with that title and year is already in the catalogue.");

        admin.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound<Error>, Conflict<Error>>> (
            Guid id, UpdateMovieCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body with { Id = id }, ct);
            if (result.IsSuccess) return TypedResults.NoContent();
            return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.Conflict(result.Error);
        }).WithName("PutMovieWithId")
        .WithSummary("Edit a film")
        .WithDescription("Every field is sent, as in the create call (stock is set separately). 409: another film already has that title and year.");

        admin.MapPatch("/{id:guid}/stock", async Task<Results<NoContent, NotFound<Error>, Conflict<Error>>> (
            Guid id, UpdateStockCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body with { Id = id }, ct);
            if (result.IsSuccess) return TypedResults.NoContent();
            return result.Error.Code == "not_found" ? TypedResults.NotFound(result.Error) : TypedResults.Conflict(result.Error);
        }).WithName("UpdateStockWithId")
        .WithSummary("Set how many copies of a film the shop has")
        .WithDescription("`totalCopies` 0 to 10000. 409: fewer than are out on rental right now.");

        admin.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound<Error>>> (
            Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(new SoftDeleteMovieCommand(id), ct);
            return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
        }).WithName("SoftDeleteWithId")
        .WithSummary("Withdraw a film (it can be restored)")
        .WithDescription("Hidden from the catalogue; rentals, reviews and bookings keep it.");

        admin.MapPost("/{id:guid}/restore", async Task<Results<NoContent, NotFound<Error>>> (
            Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(new RestoreMovieCommand(id), ct);
            return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
        }).WithName("RestoreWithId")
        .WithSummary("Put a withdrawn film back in the catalogue");
    }
}

internal static class MovieMapper
{
    public static MovieListItem ToListItem(this Movie m) => new(
        m.Id, m.Title, m.Slug, m.Genre, m.ReleaseYear, m.DurationMinutes, m.DailyPrice,
        m.AvailableCopies, m.TotalCopies, m.AverageRating, m.ReviewCount, m.PosterUrl, m.IsDeleted,
        !string.IsNullOrWhiteSpace(m.VideoUrl), m.TrailerUrl);
}
