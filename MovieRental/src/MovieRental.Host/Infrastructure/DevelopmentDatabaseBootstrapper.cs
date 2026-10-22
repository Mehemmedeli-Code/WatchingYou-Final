using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MovieRental.Modules.Catalog.Domain;
using MovieRental.Modules.Catalog.Persistence;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.Modules.Media.Persistence;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// Creates each module's schema on first run so the project starts with nothing but a
/// connection string. Production uses `dotnet ef database update` per module instead;
/// see the README. Each context owns disjoint tables, so they can be created in turn
/// against the same database.
/// </summary>
public static class DevelopmentDatabaseBootstrapper
{
    /// <summary>
    /// Bump this whenever an entity changes shape. The development database is then dropped
    /// and rebuilt on next start, because the table-exists check below would otherwise skip
    /// a schema that is present but out of date — which fails later, at query time, with a
    /// far less obvious error. Production uses real migrations and never reads this.
    /// </summary>
    private const string SchemaStamp = "2026-09-12-security-galleries-i18n";

    public static async Task InitialiseAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbBootstrap");

        await DropIfStaleAsync(scope.ServiceProvider, logger, ct);

        var contexts = new DbContext[]
        {
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>(),
            scope.ServiceProvider.GetRequiredService<RentalsDbContext>(),
            scope.ServiceProvider.GetRequiredService<CinemaDbContext>(),
            scope.ServiceProvider.GetRequiredService<MediaDbContext>()
        };

        foreach (var context in contexts)
        {
            var creator = (RelationalDatabaseCreator)context.Database.GetService<IDatabaseCreator>();
            if (!await creator.ExistsAsync(ct)) await creator.CreateAsync(ct);

            var schema = context.Model.GetDefaultSchema() ?? "dbo";
            if (await SchemaHasTablesAsync(context, schema, ct)) continue;

            await creator.CreateTablesAsync(ct);
            logger.LogInformation("Created tables for schema {Schema}.", schema);
        }

        await SeedAsync(scope.ServiceProvider, ct);
    }

    /// <summary>Compares the stored stamp with the current one and wipes the database when
    /// they differ. Only ever runs in Development — the caller is inside that branch.</summary>
    private static async Task DropIfStaleAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        var context = services.GetRequiredService<IdentityDbContext>();
        var creator = (RelationalDatabaseCreator)context.Database.GetService<IDatabaseCreator>();
        if (!await creator.ExistsAsync(ct)) return;

        string? stored = null;
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            await using var read = context.Database.GetDbConnection().CreateCommand();
            read.CommandText = """
                IF OBJECT_ID('dbo.__SchemaStamp', 'U') IS NOT NULL
                    SELECT TOP 1 [Stamp] FROM dbo.__SchemaStamp;
                """;
            stored = await read.ExecuteScalarAsync(ct) as string;
        }
        catch (DbException) { /* treated as "no stamp" */ }
        finally { await context.Database.CloseConnectionAsync(); }

        if (stored == SchemaStamp) return;

        logger.LogWarning("Schema stamp changed ({Old} -> {New}). Rebuilding the development database.",
            stored ?? "none", SchemaStamp);

        await context.Database.EnsureDeletedAsync(ct);
        await creator.CreateAsync(ct);

        await context.Database.OpenConnectionAsync(ct);
        try
        {
            await using var write = context.Database.GetDbConnection().CreateCommand();
            write.CommandText = """
                CREATE TABLE dbo.__SchemaStamp ([Stamp] NVARCHAR(128) NOT NULL);
                INSERT INTO dbo.__SchemaStamp ([Stamp]) VALUES (@stamp);
                """;
            var parameter = write.CreateParameter();
            parameter.ParameterName = "@stamp";
            parameter.Value = SchemaStamp;
            write.Parameters.Add(parameter);
            await write.ExecuteNonQueryAsync(ct);
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static async Task<bool> SchemaHasTablesAsync(DbContext context, string schema, CancellationToken ct)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = @schema";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@schema";
        parameter.Value = schema;
        command.Parameters.Add(parameter);

        await context.Database.OpenConnectionAsync(ct);
        try { return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) > 0; }
        catch (DbException) { return false; }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static async Task SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        var identity = services.GetRequiredService<IdentityDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();

        if (!await identity.Users.AnyAsync(ct))
        {
            identity.Users.AddRange(
                new AppUser
                {
                    Email = "admin@reelandrow.test", FullName = "Rena Alverdiyeva",
                    PasswordHash = hasher.Hash("Admin1234"), IsEmailConfirmed = true,
                    Roles = $"{AppRoles.Admin},{AppRoles.Customer}"
                },
                new AppUser
                {
                    Email = "security@reelandrow.test", FullName = "Kamran Hasanli",
                    PasswordHash = hasher.Hash("Security1234"), IsEmailConfirmed = true,
                    Roles = $"{AppRoles.Security},{AppRoles.Customer}"
                },
                new AppUser
                {
                    Email = "customer@reelandrow.test", FullName = "Tural Mammadov",
                    PasswordHash = hasher.Hash("Customer1234"), IsEmailConfirmed = true,
                    Roles = AppRoles.Customer
                });
            await identity.SaveChangesAsync(ct);
        }

        var catalog = services.GetRequiredService<CatalogDbContext>();
        if (!await catalog.Movies.AnyAsync(ct))
        {
            var seedFile = Path.Combine(AppContext.BaseDirectory, "seed", "movies.json");
            if (File.Exists(seedFile))
            {
                var items = System.Text.Json.JsonSerializer.Deserialize<List<MovieRental.Modules.Catalog.Features.MovieSeedItem>>(
                    await File.ReadAllTextAsync(seedFile, ct),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [];

                catalog.Movies.AddRange(items.Select(i => new Movie
                {
                    Title = i.Title, Slug = SlugFactory.Create(i.Title, i.Year), Description = i.Description,
                    Genre = i.Genre, ReleaseYear = i.Year, DurationMinutes = i.DurationMinutes, Director = i.Director,
                    PosterUrl = i.PosterUrl, TrailerUrl = i.TrailerUrl, DailyPrice = i.DailyPrice,
                    TotalCopies = i.Copies, AvailableCopies = i.Copies
                }));
                await catalog.SaveChangesAsync(ct);
            }
        }

        var cinema = services.GetRequiredService<CinemaDbContext>();
        if (!await cinema.Screenings.AnyAsync(ct))
        {
            var showcase = await catalog.Movies.OrderBy(m => m.Title).Take(5).ToListAsync(ct);
            var slot = DateTime.UtcNow.Date.AddDays(1).AddHours(15);

            // Each film gets several performances across languages and days, so Movies on
            // Display has real variety to group and filter rather than one row per film.
            var languages = new[]
            {
                ("az", (string?)null), ("en", "az"), ("ru", "az"), ("tr", "en"), ("en", "ru")
            };

            foreach (var (movie, index) in showcase.Select((m, i) => (m, i)))
            {
                for (var slotIndex = 0; slotIndex < 3; slotIndex++)
                {
                    var (audio, subtitles) = languages[(index + slotIndex) % languages.Length];
                    cinema.Screenings.Add(new Screening
                    {
                        MovieId = movie.Id,
                        MovieTitle = movie.Title,
                        Hall = slotIndex switch { 0 => "Hall A — Dolby", 1 => "Hall B", _ => "Rooftop" },
                        StartsAtUtc = slot.AddDays(slotIndex).AddHours(index * 2),
                        Rows = 8, SeatsPerRow = 12,
                        SeatPrice = 8.50m + slotIndex,
                        AudioLanguage = audio,
                        SubtitleLanguage = subtitles
                    });
                }
            }

            await cinema.SaveChangesAsync(ct);
        }
    }
}
