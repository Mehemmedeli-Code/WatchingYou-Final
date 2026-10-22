using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

/// <summary>
/// Films are served through this endpoint rather than from wwwroot. Static hosting would
/// make every upload readable by anyone who can guess a filename, which defeats the whole
/// private/public switch — so each request is authorised individually.
/// </summary>
public static class StreamShortFilmEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/shorts/{id:guid}/stream", async (
            Guid id, MediaDbContext db, ICurrentUser currentUser,
            IHostEnvironment environment, HttpContext context, CancellationToken ct) =>
        {
            var film = await db.ShortFilms.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (film is null) return Results.NotFound();

            if (!ViewerRules.MayWatch(film, currentUser))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var path = ShortFilmStorage.PathFor(environment, film.StoredFileName);
            if (!File.Exists(path)) return Results.NotFound();

            // Counted once per opening request, not once per range request, or every seek
            // would inflate the number.
            if (film.IsPublished && currentUser.Id != film.UserId &&
                !context.Request.Headers.ContainsKey("Range"))
            {
                film.ViewCount++;
                await db.SaveChangesAsync(ct);
            }

            var stream = File.OpenRead(path);
            return Results.File(stream, film.ContentType, enableRangeProcessing: true);
        })
        .WithName("StreamShortFilm").WithTags("Shorts").AllowAnonymous();
}
