using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Cqrs;

namespace MovieRental.Modules.Catalog.Features;

public sealed record GetMovieByIdQuery(Guid Id) : IQuery<MovieDetail?>;

internal sealed class GetMovieByIdHandler(CatalogDbContext db) : IQueryHandler<GetMovieByIdQuery, MovieDetail?>
{
    public async Task<MovieDetail?> Handle(GetMovieByIdQuery query, CancellationToken ct) =>
        await db.Movies
            .AsNoTracking()
            .Where(m => m.Id == query.Id)
            .Select(m => new MovieDetail(
                m.Id, m.Title, m.Slug, m.Description, m.Genre, m.ReleaseYear, m.DurationMinutes,
                m.Director, m.PosterUrl, m.TrailerUrl, m.DailyPrice, m.AvailableCopies, m.TotalCopies,
                m.AverageRating, m.ReviewCount,
                m.Reviews.OrderByDescending(r => r.CreatedAtUtc)
                    .Select(r => new ReviewResponse(r.Id, r.UserId, r.AuthorName, r.Stars, r.Comment, r.CreatedAtUtc))
                    .ToList()))
            .FirstOrDefaultAsync(ct);
}

public static class GetMovieByIdEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/movies/{id:guid}",
            async Task<Results<Ok<MovieDetail>, NotFound>> (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var movie = await dispatcher.Ask(new GetMovieByIdQuery(id), ct);
                return movie is null ? TypedResults.NotFound() : TypedResults.Ok(movie);
            })
        .WithName("GetMovieById").WithTags("Catalog").AllowAnonymous();
}
