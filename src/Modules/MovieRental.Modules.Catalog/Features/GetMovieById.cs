using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

public sealed record GetMovieByIdQuery(Guid Id) : IQuery<MovieDetail?>;

internal sealed class GetMovieByIdHandler(CatalogDbContext db, IHostEnvironment environment) : IQueryHandler<GetMovieByIdQuery, MovieDetail?>
{
    public async Task<MovieDetail?> Handle(GetMovieByIdQuery query, CancellationToken ct)
    {
        var movie = await db.Movies
            .AsNoTracking()
            .Where(m => m.Id == query.Id)
            .Select(m => new MovieDetail(
                m.Id, m.Title, m.Slug, m.Description, m.Genre, m.ReleaseYear, m.DurationMinutes,
                // The video address is never in the public (cached) detail: it is handed out by
                // /api/movies/{id}/watch, only to someone with a rental or Watching PRO.
                m.Director, m.PosterUrl, m.TrailerUrl, null, m.DailyPrice, m.AvailableCopies, m.TotalCopies,
                m.AverageRating, m.ReviewCount,
                m.Reviews.OrderByDescending(r => r.CreatedAtUtc)
                    .Select(r => new ReviewResponse(r.Id, r.UserId, r.AuthorName, r.Stars, r.Comment, r.CreatedAtUtc))
                    .ToList(),
                m.VideoUrl != null && m.VideoUrl != ""))
            .FirstOrDefaultAsync(ct);
        return movie is null ? null : movie with { Descriptions = MovieDescriptions.For(environment, movie.Slug), Cast = MovieCast.For(environment, movie.Slug) };
    }
}

public static class GetMovieByIdEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/movies/{id:guid}",
            async Task<Results<Ok<MovieDetail>, NotFound<Error>>> (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var movie = await dispatcher.Ask(new GetMovieByIdQuery(id), ct);
                return movie is null ? TypedResults.NotFound(Error.NotFound("Movie")) : TypedResults.Ok(movie);
            })
        .WithName("GetMovieById").WithTags("Catalog").AllowAnonymous().CacheOutput(AppPolicies.CatalogueCache)
        .WithSummary("One film, with its reviews")
        .WithDescription("The video address is not here: /api/movies/{id}/watch hands it out to those who may watch.");
}
