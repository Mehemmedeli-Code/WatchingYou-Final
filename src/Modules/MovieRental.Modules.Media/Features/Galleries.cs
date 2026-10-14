using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

/// <summary>
/// The two public galleries. Origin decides which one a film lands in; ViewerRules.IsListed
/// decides which approved films a stranger may see.
/// </summary>
public sealed record GetGalleryQuery(ShortFilmOrigin Origin) : IQuery<IReadOnlyList<GalleryFilm>>;

/// <summary>A gallery card: the film, who made it (their handle opens their profile), and its likes.</summary>
public sealed record GalleryFilm(ShortFilmSummary Film, string? AuthorUsername, string? AuthorAvatarUrl, int Likes, bool LikedByMe);

internal sealed class GetGalleryHandler(MediaDbContext db, IUserDirectory users, ICurrentUser currentUser)
    : IQueryHandler<GetGalleryQuery, IReadOnlyList<GalleryFilm>>
{
    public async Task<IReadOnlyList<GalleryFilm>> Handle(GetGalleryQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Approved films, newest first; then the ones a private account kept for its followers
        // are dropped (ViewerRules.IsListed). A few extra are read so the page still fills.
        // ponytail: over-reads 3x and filters in memory; move the rule into SQL if private
        // accounts ever hold most of the approved films.
        var candidates = await db.ShortFilms.AsNoTracking()
            .Where(f => f.Origin == query.Origin && f.Status == SubmissionStatus.Approved)
            .OrderByDescending(f => f.ApprovedAtUtc)
            .Take(180)
            .ToListAsync(ct);
        var privateOwners = await users.PrivateAccountsAsync([.. candidates.Select(f => f.UserId).Distinct()], ct);
        var films = candidates.Where(f => ViewerRules.IsListed(f, privateOwners.Contains(f.UserId))).Take(60).ToList();

        var ids = films.Select(f => f.Id).ToList();
        var likes = await db.ShortFilmLikes.AsNoTracking().Where(l => ids.Contains(l.ShortFilmId))
            .GroupBy(l => l.ShortFilmId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var me = currentUser.Id;
        var mine = me is null ? [] : await db.ShortFilmLikes.AsNoTracking()
            .Where(l => l.UserId == me && ids.Contains(l.ShortFilmId)).Select(l => l.ShortFilmId).ToListAsync(ct);
        var authors = await users.GetContactsAsync([.. films.Select(f => f.UserId).Distinct()], ct);

        return [.. films.Select(f => new GalleryFilm(
            f.ToSummary(now), authors.GetValueOrDefault(f.UserId)?.Username, authors.GetValueOrDefault(f.UserId)?.AvatarUrl,
            likes.GetValueOrDefault(f.Id), mine.Contains(f.Id)))];
    }
}

/// <summary>Like or unlike a film anyone may watch. Returns the new count.</summary>
public sealed record LikeShortFilmCommand(Guid FilmId, bool Like) : ICommand<int?>;

internal sealed class LikeShortFilmHandler(MediaDbContext db, IUserDirectory users, ICurrentUser currentUser)
    : ICommandHandler<LikeShortFilmCommand, int?>
{
    public async Task<int?> Handle(LikeShortFilmCommand command, CancellationToken ct)
    {
        var film = await db.ShortFilms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.FilmId, ct);
        // Only a published film someone is allowed to watch can be liked: no liking your way
        // into a film still in review, or one a private account keeps for its followers.
        if (film is null || film.Status != SubmissionStatus.Approved || !await ViewerRules.MayWatchAsync(film, currentUser, users, ct))
            return null;

        var me = currentUser.RequireId();
        var existing = await db.ShortFilmLikes.FirstOrDefaultAsync(l => l.ShortFilmId == film.Id && l.UserId == me, ct);
        if (command.Like && existing is null) db.ShortFilmLikes.Add(new ShortFilmLike { ShortFilmId = film.Id, UserId = me });
        if (!command.Like && existing is not null) db.ShortFilmLikes.Remove(existing);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (command.Like) { /* liked twice at once: the other request won, same result */ }

        return await db.ShortFilmLikes.CountAsync(l => l.ShortFilmId == film.Id, ct);
    }
}

public static class GalleryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/gallery/ai", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGalleryQuery(ShortFilmOrigin.AiGenerated), ct)))
            .WithName("GetAiCatalog").WithTags("Galleries").AllowAnonymous();

        app.MapGet("/api/gallery/human", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetGalleryQuery(ShortFilmOrigin.HandCrafted), ct)))
            .WithName("GetHumanCraft").WithTags("Galleries").AllowAnonymous();

        var like = app.MapGroup("/api/shorts/{id:guid}/like").WithTags("Galleries")
            .RequireAuthorization().RequireRateLimiting(AppPolicies.WriteRateLimit);
        like.MapPut("", async Task<Results<Ok<LikeCount>, NotFound>> (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                await dispatcher.Send(new LikeShortFilmCommand(id, true), ct) is { } n ? TypedResults.Ok(new LikeCount(n)) : TypedResults.NotFound())
            .WithName("LikeShortFilm");
        like.MapDelete("", async Task<Results<Ok<LikeCount>, NotFound>> (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                await dispatcher.Send(new LikeShortFilmCommand(id, false), ct) is { } n ? TypedResults.Ok(new LikeCount(n)) : TypedResults.NotFound())
            .WithName("UnlikeShortFilm");
    }
}

public sealed record LikeCount(int Likes);
