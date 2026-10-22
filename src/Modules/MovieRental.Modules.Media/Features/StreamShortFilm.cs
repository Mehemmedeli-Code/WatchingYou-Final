using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

/// <summary>
/// Films are served through this endpoint rather than from wwwroot. Static hosting would
/// make every upload readable by anyone who can guess a filename, which defeats the whole
/// private/public switch — so each request is authorised individually.
/// </summary>
public static class StreamShortFilmEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shorts/{id:guid}/stream", async (
            Guid id, IDispatcher dispatcher, IHostEnvironment environment, HttpContext context, CancellationToken ct) =>
        {
            var film = await dispatcher.Ask(new ShortFilmFileQuery(id), ct);
            if (film.IsFailure) return film.Error.ToHttp();

            var path = ShortFilmStorage.PathFor(environment, film.Value);
            if (!File.Exists(path)) return Results.NotFound(Error.NotFound("Film file"));

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            var stream = File.OpenRead(path);
            return Results.File(stream, ShortFilmStorage.ContentTypeFor(film.Value), enableRangeProcessing: true);
        })
        .WithName("StreamShortFilm").WithTags("Shorts").AllowAnonymous()
        .WithSummary("Play a short film (the video file, seekable)")
        .WithDescription("Anyone for an approved film in a gallery; followers for one kept for followers; always the maker, " +
                         "Security and admins. 403 otherwise. Fetching the file is not a view: see CountShortFilmView.")
        .Produces(StatusCodes.Status200OK, typeof(Stream), "video/mp4", "video/webm")
        .Produces(StatusCodes.Status206PartialContent, typeof(Stream), "video/mp4", "video/webm")
        .Produces<Error>(StatusCodes.Status403Forbidden)
        .Produces<Error>(StatusCodes.Status404NotFound);

        // A view is a play. The stream could not tell: browsers ask for it with a Range header
        // from the first byte (so a "no Range" rule counted nothing), and a page of films fetches
        // each one's opening without anybody pressing play. The player says when it starts.
        app.MapPost("/api/shorts/{id:guid}/view", async Task<Results<Ok<FilmViews>, BadRequest<Error>, NotFound<Error>>> (
            Guid id, IDispatcher dispatcher, HttpContext context, CancellationToken ct) =>
        {
            // JSON only. A bare form post is something any other site can make its visitors'
            // browsers send, which would let it count views for films nobody watched; a JSON
            // post from another origin needs a preflight, and CORS refuses it.
            if (!context.Request.HasJsonContentType())
                return TypedResults.BadRequest(Error.Validation("Send an empty JSON object (Content-Type: application/json)."));

            var result = await dispatcher.Send(new CountShortFilmViewCommand(id), ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.NotFound(result.Error);
        })
        .WithName("CountShortFilmView").WithTags("Shorts").AllowAnonymous()
        .RequireRateLimiting(AppPolicies.WriteRateLimit)
        .WithSummary("Count one play of a published film")
        .WithDescription("Body: {} as JSON (a plain form post is refused with 400). The player sends it when a film starts, and again when it is played once more after it " +
                         "ended; pausing and seeking stay the same view. Every play counts, the maker's too. Answers with " +
                         "the new count. 404: no such published film, or one you may not watch.");
    }
}

public sealed record FilmViews(int Views);

/// <summary>The stored file name of a film the caller may watch.</summary>
public sealed record ShortFilmFileQuery(Guid Id) : IQuery<Result<string>>;

internal sealed class ShortFilmFileHandler(MediaDbContext db, ICurrentUser currentUser, IUserDirectory users)
    : IQueryHandler<ShortFilmFileQuery, Result<string>>
{
    public async Task<Result<string>> Handle(ShortFilmFileQuery query, CancellationToken ct)
    {
        var film = await db.ShortFilms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.Id, ct);
        if (film is null) return Result.Failure<string>(Error.NotFound("Film"));
        if (!await ViewerRules.MayWatchAsync(film, currentUser, users, ct))
            return Result.Failure<string>(Error.Forbidden("This film is not out yet, or its maker shares it with followers only."));
        return film.StoredFileName;
    }
}

public sealed record CountShortFilmViewCommand(Guid Id) : ICommand<Result<FilmViews>>;

internal sealed class CountShortFilmViewHandler(MediaDbContext db, ICurrentUser currentUser, IUserDirectory users)
    : ICommandHandler<CountShortFilmViewCommand, Result<FilmViews>>
{
    public async Task<Result<FilmViews>> Handle(CountShortFilmViewCommand command, CancellationToken ct)
    {
        var film = await db.ShortFilms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (film is null || film.Status != Domain.SubmissionStatus.Approved || !await ViewerRules.MayWatchAsync(film, currentUser, users, ct))
            return Result.Failure<FilmViews>(Error.NotFound("Film"));

        // One UPDATE in the database, so plays at the same moment all count.
        await db.ShortFilms.Where(f => f.Id == command.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ViewCount, f => f.ViewCount + 1), ct);
        return new FilmViews(await db.ShortFilms.Where(f => f.Id == command.Id).Select(f => f.ViewCount).FirstAsync(ct));
    }
}
