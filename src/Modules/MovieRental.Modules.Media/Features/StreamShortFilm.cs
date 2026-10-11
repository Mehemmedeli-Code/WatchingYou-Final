using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
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
            Guid id, MediaDbContext db, ICurrentUser currentUser, IUserDirectory users,
            IHostEnvironment environment, HttpContext context, CancellationToken ct) =>
        {
            var film = await db.ShortFilms.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (film is null) return Results.NotFound();

            if (!await ViewerRules.MayWatchAsync(film, currentUser, users, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var path = ShortFilmStorage.PathFor(environment, film.StoredFileName);
            if (!File.Exists(path)) return Results.NotFound();

            // Counted once per opening request, not once per range request, or every seek
            // would inflate the number.
            // And once per viewer every few hours: reloading the page used to add a view each time.
            var viewer = currentUser.Id?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "";
            if (film.Status == Domain.SubmissionStatus.Approved && currentUser.Id != film.UserId &&
                !context.Request.Headers.ContainsKey("Range") && RecentViews.FirstIn(film.Id, viewer))
            {
                film.ViewCount++;
                await db.SaveChangesAsync(ct);
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            var stream = File.OpenRead(path);
            return Results.File(stream, ShortFilmStorage.ContentTypeFor(film.StoredFileName), enableRangeProcessing: true);
        })
        .WithName("StreamShortFilm").WithTags("Shorts").AllowAnonymous();
}

/// <summary>Who has been counted for which film lately.</summary>
// ponytail: in memory on one server; a restart forgets it, and several servers would each count once.
internal static class RecentViews
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(6);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, string), DateTime> Seen = new();

    public static bool FirstIn(Guid filmId, string viewer)
    {
        var now = DateTime.UtcNow;
        if (Seen.Count > 50_000)
            foreach (var old in Seen.Where(p => now - p.Value > Window).Select(p => p.Key).ToList()) Seen.TryRemove(old, out _);

        var counted = false;
        Seen.AddOrUpdate((filmId, viewer),
            _ => { counted = true; return now; },
            (_, last) => { if (now - last <= Window) return last; counted = true; return now; });
        return counted;
    }
}
