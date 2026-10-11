using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

// The film grid on a profile, in place of Instagram's posts: the member's approved films, for
// whoever may open the profile (anyone on a public account; on a private one, the people it
// accepted). Each film says whether it is also out in its gallery (Listed), which on a private
// account is the owner's choice per film. Films still in review stay in Studio.
/// <summary>One square of the grid. No reviewer notes or file names: other people see this.</summary>
public sealed record ProfileFilm(
    Guid Id, string Title, string Synopsis, ShortFilmOrigin Origin, ShortFilmVisibility Visibility,
    SubmissionStatus Status, int ViewCount, DateTime SubmittedAtUtc, string StreamUrl);

public sealed record ProfileFilms(IReadOnlyList<ProfileFilm> Items, int PublicCount);

public static class ProfileFilmsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/shorts/by/{userId:guid}", async (Guid userId, string? origin,
                MediaDbContext db, ICurrentUser currentUser, IUserDirectory users, CancellationToken ct) =>
            {
                if (!await users.MayViewProfileAsync(currentUser.RequireId(), userId, ct))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                var films = db.ShortFilms.AsNoTracking().Where(f => f.UserId == userId && f.Status == SubmissionStatus.Approved);
                films = origin?.ToLowerInvariant() switch
                {
                    "ai" => films.Where(f => f.Origin == ShortFilmOrigin.AiGenerated),
                    "human" => films.Where(f => f.Origin == ShortFilmOrigin.HandCrafted),
                    _ => films
                };

                // ponytail: newest 120, no paging; page it when someone actually posts that many.
                var items = await films.OrderByDescending(f => f.ApprovedAtUtc).Take(120)
                    .Select(f => new ProfileFilm(f.Id, f.Title, f.Synopsis, f.Origin, f.Visibility, f.Status,
                        f.ViewCount, f.SubmittedAtUtc, "/api/shorts/" + f.Id + "/stream"))
                    .ToListAsync(ct);
                var count = await db.ShortFilms.CountAsync(f => f.UserId == userId && f.Status == SubmissionStatus.Approved, ct);
                return Results.Ok(new ProfileFilms(items, count));
            })
            .WithName("GetProfileFilms").WithTags("Shorts").RequireAuthorization();
}