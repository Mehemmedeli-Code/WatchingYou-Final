using MovieRental.Modules.Catalog.Domain;

namespace MovieRental.Tests;

public class RecommendationEngineTests
{
    private static readonly Guid DramaGood = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DramaWeak = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Western = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Seen = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private static readonly RecommendationCandidate[] Candidates =
    [
        new(DramaGood, "Drama", 4.6, 12),
        new(DramaWeak, "Drama", 2.1, 3),
        new(Western, "Western", 4.9, 40),
        new(Seen, "Drama", 5.0, 50),
    ];

    [Fact]
    public void A_liked_genre_outranks_a_better_reviewed_film_from_elsewhere()
    {
        var signals = new[] { new TasteSignal("Drama", TasteSignalKind.Rented), new TasteSignal("Drama", TasteSignalKind.Watchlisted) };

        var ranked = RecommendationEngine.Rank(Candidates, signals, new HashSet<Guid> { Seen }, 3);

        Assert.Equal(DramaGood, ranked[0].Id);
        Assert.Equal("Drama", ranked[0].BecauseGenre);
    }

    [Fact]
    public void Films_already_rented_or_saved_are_never_suggested()
    {
        var ranked = RecommendationEngine.Rank(Candidates, [new TasteSignal("Drama", TasteSignalKind.Rented)],
            new HashSet<Guid> { Seen }, 10);

        Assert.DoesNotContain(Seen, ranked.Select(r => r.Id));
    }

    [Fact]
    public void Without_history_the_list_is_quality_order_with_no_reason_attached()
    {
        var ranked = RecommendationEngine.Rank(Candidates, [], new HashSet<Guid>(), 2);

        Assert.Equal(Seen, ranked[0].Id);                // 5.0 from fifty reviews
        Assert.All(ranked, r => Assert.Null(r.BecauseGenre));
    }

    [Fact]
    public void A_bad_review_pushes_a_genre_down()
    {
        var affinity = RecommendationEngine.GenreAffinity(
        [
            new TasteSignal("Western", TasteSignalKind.Rented),
            new TasteSignal("Western", TasteSignalKind.Reviewed, 1),
            new TasteSignal("Drama", TasteSignalKind.Rented),
        ]);

        Assert.True(affinity["Drama"] > affinity["Western"]);
        Assert.Equal(1.0, affinity["Drama"]);
    }
}
