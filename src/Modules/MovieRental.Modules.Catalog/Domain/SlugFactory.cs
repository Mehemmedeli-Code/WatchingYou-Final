using MovieRental.SharedKernel;

namespace MovieRental.Modules.Catalog.Domain;

public static class SlugFactory
{
    /// <summary>The title as a slug, with the year so two films of one name stay apart.</summary>
    public static string Create(string title, int year) => $"{Slug.From(title)}-{year}";
}
