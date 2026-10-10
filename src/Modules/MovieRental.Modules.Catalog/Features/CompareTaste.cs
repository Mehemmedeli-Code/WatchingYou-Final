using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

// "How alike are our tastes?" — the question that makes a stranger on a map worth talking to.
//
// Catalog answers it because Catalog owns genres. Rentals supplies the film ids and never
// learns what a genre is.

public sealed record GenreShare(string Genre, int Mine, int Theirs);

public sealed record TasteComparison(
    Guid OtherUserId, string OtherName,
    int MatchPercent, int SharedTitles, int MyTotal, int TheirTotal,
    IReadOnlyList<GenreShare> Genres, IReadOnlyList<string> SharedFilms);

public sealed record CompareTasteQuery(Guid OtherUserId) : IQuery<TasteComparison?>;

internal sealed class CompareTasteHandler(
    CatalogDbContext db, ICurrentUser currentUser, IRentalApi rentals, IUserDirectory users)
    : IQueryHandler<CompareTasteQuery, TasteComparison?>
{
    public async Task<TasteComparison?> Handle(CompareTasteQuery query, CancellationToken ct)
    {
        var meId = currentUser.RequireId();
        var them = await users.GetContactAsync(query.OtherUserId, ct);
        // Only people who chose to appear on the globe: otherwise any user id (they are public
        // on reviews) gave away that person's name and what they have rented.
        if (them is null || !them.ShareOnGlobe) return null;

        var mine = await rentals.RentedMovieIdsAsync(meId, ct);
        var theirs = await rentals.RentedMovieIdsAsync(query.OtherUserId, ct);

        var all = mine.Concat(theirs).Distinct().ToArray();
        var films = await db.Movies.AsNoTracking()
            .IgnoreQueryFilters()          // a withdrawn title still shaped somebody's taste
            .Where(m => all.Contains(m.Id))
            .Select(m => new { m.Id, m.Title, m.Genre })
            .ToListAsync(ct);

        var byId = films.ToDictionary(f => f.Id);
        var mineSet = mine.ToHashSet();
        var theirsSet = theirs.ToHashSet();

        var genres = films
            .Select(f => f.Genre)
            .Distinct()
            .Select(genre => new GenreShare(
                genre,
                mine.Count(id => byId.TryGetValue(id, out var f) && f.Genre == genre),
                theirs.Count(id => byId.TryGetValue(id, out var f) && f.Genre == genre)))
            .Where(g => g.Mine > 0 || g.Theirs > 0)
            .OrderByDescending(g => g.Mine + g.Theirs)
            .Take(8)
            .ToArray();

        // Jaccard over genres rather than over titles. Two people who have watched no film in
        // common can still both live on westerns and documentaries, and that is the thing
        // worth knowing before you start a conversation.
        var myGenres = genres.Where(g => g.Mine > 0).Select(g => g.Genre).ToHashSet();
        var theirGenres = genres.Where(g => g.Theirs > 0).Select(g => g.Genre).ToHashSet();
        var union = myGenres.Union(theirGenres).Count();
        var overlap = myGenres.Intersect(theirGenres).Count();

        var match = union == 0 ? 0 : (int)Math.Round(overlap * 100.0 / union);

        var sharedFilms = mineSet.Intersect(theirsSet)
            .Select(id => byId.TryGetValue(id, out var f) ? f.Title : null)
            .Where(title => title is not null)
            .Take(6)
            .ToArray()!;

        return new TasteComparison(
            query.OtherUserId, them.FullName, match, mineSet.Intersect(theirsSet).Count(),
            mine.Count, theirs.Count, genres, [.. sharedFilms!]);
    }
}

public static class CompareTasteEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/taste/compare/{userId:guid}", async (
                Guid userId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var comparison = await dispatcher.Ask(new CompareTasteQuery(userId), ct);
                return comparison is null ? Results.NotFound() : Results.Ok(comparison);
            })
            .WithName("CompareTasteWithId").WithTags("Globe").RequireAuthorization();
}
