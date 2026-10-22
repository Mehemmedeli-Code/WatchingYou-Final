using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Catalog.Features;

/// <summary>Shape of an external movie payload. Kept separate from the entity so a change
/// in the feed never forces a schema change.</summary>
public sealed class MovieSeedItem
{
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("genre")] public string Genre { get; set; } = "Drama";
    [JsonPropertyName("year")] public int Year { get; set; }
    [JsonPropertyName("durationMinutes")] public int DurationMinutes { get; set; } = 100;
    [JsonPropertyName("director")] public string? Director { get; set; }
    [JsonPropertyName("posterUrl")] public string? PosterUrl { get; set; }
    [JsonPropertyName("trailerUrl")] public string? TrailerUrl { get; set; }
    [JsonPropertyName("dailyPrice")] public decimal DailyPrice { get; set; } = 2.50m;
    [JsonPropertyName("copies")] public int Copies { get; set; } = 3;
}

public sealed record SeedMoviesCommand(IReadOnlyList<MovieSeedItem> Items) : ICommand<Result<SeedSummary>>;

public sealed record SeedSummary(int Inserted, int Updated, int Skipped);

internal sealed class SeedMoviesHandler(CatalogDbContext db) : ICommandHandler<SeedMoviesCommand, Result<SeedSummary>>
{
    public async Task<Result<SeedSummary>> Handle(SeedMoviesCommand command, CancellationToken ct)
    {
        if (command.Items.Count == 0)
            return Result.Failure<SeedSummary>(Error.Validation("The payload contained no movies."));

        int inserted = 0, updated = 0, skipped = 0;

        // One transaction for the whole batch: a half-imported catalogue is worse than
        // none, and the caller can safely retry after a failure.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var item in command.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || item.Year <= 0) { skipped++; continue; }

            var slug = SlugFactory.Create(item.Title, item.Year);
            var existing = await db.Movies.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Slug == slug, ct);

            if (existing is null)
            {
                db.Movies.Add(new Movie
                {
                    Title = item.Title.Trim(),
                    Slug = slug,
                    Description = item.Description,
                    Genre = item.Genre,
                    ReleaseYear = item.Year,
                    DurationMinutes = item.DurationMinutes,
                    Director = item.Director,
                    PosterUrl = item.PosterUrl,
                    TrailerUrl = item.TrailerUrl,
                    DailyPrice = item.DailyPrice,
                    TotalCopies = item.Copies,
                    AvailableCopies = item.Copies
                });
                inserted++;
            }
            else
            {
                existing.Description = item.Description;
                existing.Genre = item.Genre;
                existing.PosterUrl = item.PosterUrl ?? existing.PosterUrl;
                existing.TrailerUrl = item.TrailerUrl ?? existing.TrailerUrl;
                existing.DailyPrice = item.DailyPrice;
                updated++;
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(new SeedSummary(inserted, updated, skipped));
    }
}

public static class SeedMoviesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Accepts either a bare JSON array or { "movies": [ ... ] }.
        app.MapPost("/api/admin/movies/seed", async (
                HttpRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);

                var array = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement
                    : document.RootElement.TryGetProperty("movies", out var wrapped) ? wrapped : default;

                if (array.ValueKind != JsonValueKind.Array)
                    return Results.BadRequest(Error.Validation("Send a JSON array, or an object with a \"movies\" array."));

                var items = array.Deserialize<List<MovieSeedItem>>(options) ?? [];
                var result = await dispatcher.Send(new SeedMoviesCommand(items), ct);

                return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
            })
        .WithName("SeedMoviesFromJson").WithTags("Catalog admin").RequireAuthorization(AppRoles.Admin);
    }
}
