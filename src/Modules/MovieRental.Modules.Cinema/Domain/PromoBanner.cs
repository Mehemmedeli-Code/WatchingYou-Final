using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

/// <summary>The four looks a board can have: the house one, and one per holiday season.</summary>
public enum BannerTheme { Brand = 1, Christmas = 2, Novruz = 3, Ramadan = 4 }

/// <summary>
/// An advertising board on the catalogue page. The art belongs to the theme; what the admin
/// changes is whether it shows, in what order, between which days, which promo code it sells
/// (its discount is read from the code, so the board and the checkout can never disagree),
/// and, if they want, its words — left empty, the theme's own words appear in the reader's
/// language.
/// </summary>
public sealed class PromoBanner : BaseEntity
{
    public BannerTheme Theme { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public string? Headline { get; set; }
    public string? Subline { get; set; }

    /// <summary>A code from the promo codes; the house board carries none.</summary>
    public string? PromoCode { get; set; }

    /// <summary>Baku days, inclusive; either end may be open.</summary>
    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }

    public bool InSeason(DateOnly today) => (StartsOn is null || StartsOn <= today) && (EndsOn is null || today <= EndsOn);
}
