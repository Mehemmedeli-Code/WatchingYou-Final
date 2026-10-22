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

public sealed record CreateMovieCommand(
    string Title, string Description, string Genre, int ReleaseYear, int DurationMinutes,
    string? Director, string? PosterUrl, string? TrailerUrl, decimal DailyPrice, int TotalCopies)
    : ICommand<Result<MovieListItem>>;

internal sealed class CreateMovieValidator : AbstractValidator<CreateMovieCommand>
{
    public CreateMovieValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Genre).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ReleaseYear).InclusiveBetween(1888, DateTime.UtcNow.Year + 5);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 600);
        RuleFor(x => x.DailyPrice).GreaterThan(0).LessThan(1000);
        RuleFor(x => x.TotalCopies).InclusiveBetween(0, 10_000);
    }
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
            DailyPrice = command.DailyPrice,
            TotalCopies = command.TotalCopies,
            AvailableCopies = command.TotalCopies
        };

        db.Movies.Add(movie);
        await db.SaveChangesAsync(ct);
        return Result.Success(movie.ToListItem());
    }
}

public sealed record UpdateMovieCommand(
    Guid Id, string Title, string Description, string Genre, int ReleaseYear, int DurationMinutes,
    string? Director, string? PosterUrl, string? TrailerUrl, decimal DailyPrice) : ICommand<Result>;

internal sealed class UpdateMovieHandler(CatalogDbContext db) : ICommandHandler<UpdateMovieCommand, Result>
{
    public async Task<Result> Handle(UpdateMovieCommand command, CancellationToken ct)
    {
        var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == command.Id, ct);
        if (movie is null) return Result.Failure(Error.NotFound("Movie"));

        movie.Title = command.Title.Trim();
        movie.Slug = SlugFactory.Create(command.Title, command.ReleaseYear);
        movie.Description = command.Description.Trim();
        movie.Genre = command.Genre.Trim();
        movie.ReleaseYear = command.ReleaseYear;
        movie.DurationMinutes = command.DurationMinutes;
        movie.Director = command.Director?.Trim();
        movie.PosterUrl = command.PosterUrl?.Trim();
        movie.TrailerUrl = command.TrailerUrl?.Trim();
        movie.DailyPrice = command.DailyPrice;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record UpdateStockCommand(Guid Id, int TotalCopies) : ICommand<Result>;

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
        }).WithName("CreateMovie");

        admin.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound<Error>>> (
            Guid id, UpdateMovieCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body with { Id = id }, ct);
            return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
        }).WithName("PutMovieWithId");

        admin.MapPatch("/{id:guid}/stock", async Task<Results<NoContent, Conflict<Error>>> (
            Guid id, UpdateStockCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body with { Id = id }, ct);
            return result.IsSuccess ? TypedResults.NoContent() : TypedResults.Conflict(result.Error);
        }).WithName("UpdateStockWithId");

        admin.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound<Error>>> (
            Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(new SoftDeleteMovieCommand(id), ct);
            return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
        }).WithName("SoftDeleteWithId");

        admin.MapPost("/{id:guid}/restore", async Task<Results<NoContent, NotFound<Error>>> (
            Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(new RestoreMovieCommand(id), ct);
            return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
        }).WithName("RestoreWithId");
    }
}

internal static class MovieMapper
{
    public static MovieListItem ToListItem(this Movie m) => new(
        m.Id, m.Title, m.Slug, m.Genre, m.ReleaseYear, m.DurationMinutes, m.DailyPrice,
        m.AvailableCopies, m.TotalCopies, m.AverageRating, m.ReviewCount, m.PosterUrl, m.IsDeleted);
}
