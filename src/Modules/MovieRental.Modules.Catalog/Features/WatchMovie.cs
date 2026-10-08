using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

/// <summary>
/// The one place a film's video address leaves the server. Allowed is false — with the reason —
/// rather than a 403, because "not yet" is an ordinary answer here: the page turns it into
/// "Rent for $0.50" or "Get Watching PRO", not into an error.
/// </summary>
/// <param name="Access">"admin", "pro", "rental", "awaitingDecision" or "none".</param>
public sealed record WatchResponse(bool Allowed, string Access, string? VideoUrl);

public sealed record WatchMovieQuery(Guid MovieId) : IQuery<WatchResponse?>;

internal sealed class WatchMovieHandler(CatalogDbContext db, ICurrentUser currentUser, IRentalApi rentals)
    : IQueryHandler<WatchMovieQuery, WatchResponse?>
{
    public async Task<WatchResponse?> Handle(WatchMovieQuery query, CancellationToken ct)
    {
        var movie = await db.Movies.AsNoTracking()
            .Where(m => m.Id == query.MovieId)
            .Select(m => new { m.VideoUrl })
            .FirstOrDefaultAsync(ct);
        if (movie is null) return null;

        // Staff run and check the site; they watch without renting or subscribing.
        if (currentUser.IsInRole(AppRoles.Admin) || currentUser.IsInRole(AppRoles.Security))
            return new WatchResponse(true, "admin", movie.VideoUrl);

        var access = await rentals.GetWatchAccessAsync(currentUser.RequireId(), query.MovieId, ct);
        return access switch
        {
            WatchAccess.Pro => new WatchResponse(true, "pro", movie.VideoUrl),
            WatchAccess.Rental => new WatchResponse(true, "rental", movie.VideoUrl),
            WatchAccess.AwaitingDecision => new WatchResponse(false, "awaitingDecision", null),
            _ => new WatchResponse(false, "none", null),
        };
    }
}

public static class WatchMovieEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/movies/{id:guid}/watch",
            async Task<Results<Ok<WatchResponse>, NotFound>> (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var answer = await dispatcher.Ask(new WatchMovieQuery(id), ct);
                return answer is null ? TypedResults.NotFound() : TypedResults.Ok(answer);
            })
        .WithName("WatchMovie").WithTags("Catalog").RequireAuthorization();
}
