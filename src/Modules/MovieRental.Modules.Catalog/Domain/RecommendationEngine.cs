namespace MovieRental.Modules.Catalog.Domain;

/// <summary>What a customer has done that says something about their taste.</summary>
public sealed record TasteSignal(string Genre, TasteSignalKind Kind, int Stars = 0);

public enum TasteSignalKind { Rented = 1, Watchlisted = 2, Reviewed = 3 }

/// <summary>A film that could be suggested, with the numbers the score needs.</summary>
public sealed record RecommendationCandidate(Guid Id, string Genre, double AverageRating, int ReviewCount);

public sealed record ScoredCandidate(Guid Id, double Score, string? BecauseGenre);

/// <summary>
/// Content-based recommendations, small enough to read in one sitting.
///
/// Every signal adds weight to a genre: a rental counts three, a watchlist entry two, a review
/// of four or five stars two more, and a review of one or two stars takes two away. A film then
/// scores by how much its genre is liked (60%), how well it is reviewed (30%) and how many
/// people reviewed it (10%) — so a well-liked genre wins, but a poorly rated film inside it
/// does not float to the top on genre alone.
///
/// No history at all falls back to plain quality ordering, which is the honest answer for a
/// stranger: we do not know you yet, here is what other people liked.
///
/// Pure and static on purpose: no database, no clock, so the tests can pin its behaviour.
/// </summary>
public static class RecommendationEngine
{
    public const double GenreWeight = 0.6;
    public const double RatingWeight = 0.3;
    public const double PopularityWeight = 0.1;

    /// <summary>Reviews beyond this many stop adding confidence.</summary>
    public const int PopularityCap = 20;

    public static IReadOnlyDictionary<string, double> GenreAffinity(IEnumerable<TasteSignal> signals)
    {
        var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var signal in signals)
        {
            var delta = signal.Kind switch
            {
                TasteSignalKind.Rented => 3.0,
                TasteSignalKind.Watchlisted => 2.0,
                TasteSignalKind.Reviewed when signal.Stars >= 4 => 2.0,
                TasteSignalKind.Reviewed when signal.Stars is > 0 and <= 2 => -2.0,
                _ => 0.0
            };

            weights[signal.Genre] = weights.GetValueOrDefault(signal.Genre) + delta;
        }

        // Normalise to 0..1 against the favourite genre. Genres that ended up negative are
        // floored at zero: disliking westerns should sink them, not invert the scale.
        var max = weights.Values.DefaultIfEmpty(0).Max();
        if (max <= 0) return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        return weights.ToDictionary(
            pair => pair.Key,
            pair => Math.Max(0, pair.Value) / max,
            StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ScoredCandidate> Rank(
        IEnumerable<RecommendationCandidate> candidates,
        IEnumerable<TasteSignal> signals,
        ISet<Guid> exclude,
        int take)
    {
        var affinity = GenreAffinity(signals);
        var hasTaste = affinity.Count > 0;

        return candidates
            .Where(c => !exclude.Contains(c.Id))
            .Select(c =>
            {
                var genre = affinity.GetValueOrDefault(c.Genre);
                var rating = Math.Clamp(c.AverageRating / 5.0, 0, 1);
                var popularity = Math.Min(c.ReviewCount, PopularityCap) / (double)PopularityCap;

                var score = hasTaste
                    ? GenreWeight * genre + RatingWeight * rating + PopularityWeight * popularity
                    : 0.75 * rating + 0.25 * popularity;

                return new ScoredCandidate(c.Id, Math.Round(score, 4), hasTaste && genre > 0 ? c.Genre : null);
            })
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Id)                       // stable order when scores tie
            .Take(Math.Max(0, take))
            .ToList();
    }
}
