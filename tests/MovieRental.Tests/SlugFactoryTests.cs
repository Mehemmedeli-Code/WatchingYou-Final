using MovieRental.Modules.Catalog.Domain;

namespace MovieRental.Tests;

/// <summary>
/// Slugs end up in URLs and carry a unique index, so two different films must not collapse
/// into the same string — and an Azerbaijani title must survive the trip.
/// </summary>
public class SlugFactoryTests
{
    [Theory]
    [InlineData("Blue Hour", 2023, "blue-hour-2023")]
    [InlineData("The Salt Road", 2019, "the-salt-road-2019")]
    [InlineData("Thirty-Six Frames", 2022, "thirty-six-frames-2022")]
    public void Titles_become_lowercase_hyphenated_slugs(string title, int year, string expected) =>
        Assert.Equal(expected, SlugFactory.Create(title, year));

    [Fact]
    public void Diacritics_are_folded_to_ascii()
    {
        // "Two Weeks in Şəki" has to produce something a URL bar will not mangle.
        Assert.Equal("two-weeks-in-seki-2024", SlugFactory.Create("Two Weeks in Şəki", 2024));
    }

    [Fact]
    public void Punctuation_collapses_rather_than_repeating()
    {
        Assert.Equal("cold-open-2025", SlugFactory.Create("Cold Open!!! ", 2025));
        Assert.Equal("dust-and-copper-2017", SlugFactory.Create("Dust & Copper", 2017));
    }

    // A cinema's photo is the file named by its slug (wwwroot/img/venues), so these must match
    // the file names there.
    [Theory]
    [InlineData("28 Mall", "28-mall")]
    [InlineData("Afimall City", "afimall-city")]
    [InlineData("Gənclik Mall", "genclik-mall")]
    [InlineData(" Nizami ", "nizami")]
    public void Cinema_names_become_photo_file_names(string name, string expected) =>
        Assert.Equal(expected, MovieRental.SharedKernel.Slug.From(name));

    [Fact]
    public void The_year_keeps_remakes_apart()
    {
        // Same title, different films. The unique index depends on this.
        Assert.NotEqual(SlugFactory.Create("Blue Hour", 1998), SlugFactory.Create("Blue Hour", 2023));
    }
}
