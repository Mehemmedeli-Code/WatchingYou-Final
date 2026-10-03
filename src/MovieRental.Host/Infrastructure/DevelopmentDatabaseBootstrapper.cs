using System.Data.Common;
using Microsoft.Data.SqlClient;
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
    private const string SchemaStamp = "2026-09-20-direct-messages";

    public static async Task InitialiseAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbBootstrap");
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Once real migrations exist, this whole file steps aside. Migrate instead of dropping:
        // a new column is added to the database you already have, rather than costing you every
        // account and booking in it. Flip Database:UseMigrations after running scripts\migrations.ps1.
        if (configuration.GetValue("Database:UseMigrations", false))
        {
            await MigrateAsync(scope.ServiceProvider, logger, ct);
            await SeedAsync(scope.ServiceProvider, ct);
            return;
        }

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
            if (!await creator.ExistsAsync(ct)) await CreateCleanAsync(context, creator, logger, ct);

            var schema = context.Model.GetDefaultSchema() ?? "dbo";
            if (await SchemaHasTablesAsync(context, schema, ct)) continue;

            await creator.CreateTablesAsync(ct);
            logger.LogInformation("Created tables for schema {Schema}.", schema);
        }

        await SeedAsync(scope.ServiceProvider, ct);
        await WriteStampAsync(scope.ServiceProvider, ct);
    }

    /// <summary>Applies each module's pending migrations. Contexts are migrated one at a time
    /// because each owns its own schema and its own __EFMigrations table — there is no single
    /// history for the solution, and that is the point of the modular split.</summary>
    private static async Task MigrateAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        DbContext[] contexts =
        [
            services.GetRequiredService<IdentityDbContext>(),
            services.GetRequiredService<CatalogDbContext>(),
            services.GetRequiredService<RentalsDbContext>(),
            services.GetRequiredService<CinemaDbContext>(),
            services.GetRequiredService<MediaDbContext>(),
        ];

        foreach (var context in contexts)
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(ct)).ToArray();
            if (pending.Length == 0) continue;

            logger.LogInformation("Applying {Count} migration(s) to {Context}.", pending.Length, context.GetType().Name);
            await context.Database.MigrateAsync(ct);
        }
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
        await CreateCleanAsync(context, creator, logger, ct);
    }

    /// <summary>
    /// Creates the database, first clearing any files a previous LocalDB instance left behind.
    ///
    /// When a LocalDB instance is deleted and recreated — the standard fix when it will not
    /// start — the new instance forgets the database but the .mdf and .ldf stay on disk. EF's
    /// EnsureDeleted only drops what the instance knows about, so it cannot see them, and the
    /// next CREATE DATABASE fails because its files already exist. Only files named exactly for
    /// this database, and only when the instance has no such database registered, are removed:
    /// in that state they belong to nothing.
    /// </summary>
    private static async Task CreateCleanAsync(
        DbContext context, RelationalDatabaseCreator creator, ILogger logger, CancellationToken ct)
    {
        await RemoveOrphanedFilesAsync(context, logger, ct);
        await creator.CreateAsync(ct);
    }

    private static async Task RemoveOrphanedFilesAsync(DbContext context, ILogger logger, CancellationToken ct)
    {
        var target = new SqlConnectionStringBuilder(context.Database.GetConnectionString());
        var database = target.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database)) return;

        var master = new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = "master" };

        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(ct);

        await using (var registered = connection.CreateCommand())
        {
            registered.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
            registered.Parameters.AddWithValue("@name", database);

            // Registered means live: its files are in use and are not ours to touch.
            if (Convert.ToInt32(await registered.ExecuteScalarAsync(ct)) > 0) return;
        }

        string? dataPath = null, logPath = null;
        await using (var paths = connection.CreateCommand())
        {
            // Where this instance puts new database files — the user profile folder for LocalDB.
            paths.CommandText = """
                SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)),
                       CAST(SERVERPROPERTY('InstanceDefaultLogPath')  AS nvarchar(512))
                """;
            await using var reader = await paths.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                dataPath = reader.IsDBNull(0) ? null : reader.GetString(0);
                logPath = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        var candidates = new[]
        {
            dataPath is null ? null : Path.Combine(dataPath, $"{database}.mdf"),
            logPath is null ? null : Path.Combine(logPath, $"{database}_log.ldf"),
        };

        foreach (var file in candidates)
        {
            if (file is null || !File.Exists(file)) continue;

            try
            {
                File.Delete(file);
                logger.LogWarning("Removed a file left over from an earlier LocalDB instance: {File}", file);
            }
            catch (IOException ex)
            {
                // Name the file and the fix, rather than failing later on a CREATE DATABASE
                // message that points at neither.
                throw new InvalidOperationException(
                    $"'{file}' is left over from an earlier LocalDB instance and is locked. " +
                    "Close SQL Server Management Studio and anything else connected to LocalDB, " +
                    "then start the app again.", ex);
            }
        }
    }

    /// <summary>Recorded only once every schema has been created and seeded, so a failure
    /// part-way through leaves no stamp and the next start rebuilds instead of trusting a
    /// database that was never finished.</summary>
    private static async Task WriteStampAsync(IServiceProvider services, CancellationToken ct)
    {
        var context = services.GetRequiredService<IdentityDbContext>();
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            await using var write = context.Database.GetDbConnection().CreateCommand();
            write.CommandText = """
                IF OBJECT_ID('dbo.__SchemaStamp', 'U') IS NULL
                    CREATE TABLE dbo.__SchemaStamp ([Stamp] NVARCHAR(128) NOT NULL);
                DELETE FROM dbo.__SchemaStamp;
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
            // Four cinemas, four rooms each. The rooms differ on purpose: a preview that
            // showed the same box everywhere would be decoration rather than information.
            var venues = new[]
            {
                ("Nizami", "Baku", "28 May küç. 1", 40.3725, 49.8375),
                ("28 Mall", "Baku", "Azadlıq pr. 15", 40.3789, 49.8480),
                ("Ganjlik Mall", "Baku", "Fatali Khan Khoyski 14", 40.4004, 49.8506),
                ("Metropark", "Baku", "Babek pr. 96", 40.4044, 49.8949),
            };

            var layouts = new (string Name, string Format, int Rows, int Cols, double Width,
                               double Depth, double Height, double Screen, double ScreenH,
                               double Curve, double FirstRow, double Pitch, double Rise)[]
            {
                ("A100", "Dolby Atmos", 8, 6, 16, 24, 8.4, 9.6, 4.2, 17, 7.4, 1.10, 0.42),
                ("B100", "Standart",    7, 5, 13, 20, 7.2, 7.8, 3.4, 22, 6.2, 1.05, 0.38),
                ("C100", "IMAX",       10, 8, 22, 30, 11.0, 15.0, 7.0, 26, 8.6, 1.20, 0.50),
                ("D100", "VIP recliner", 5, 4, 11, 16, 6.6, 6.6, 2.9, 40, 5.4, 1.40, 0.55),
            };

            var halls = new List<Hall>();

            foreach (var (name, city, address, lat, lng) in venues)
            {
                var venue = new Venue
                {
                    Name = name, City = city, Address = address,
                    Latitude = lat, Longitude = lng
                };
                foreach (var layout in layouts)
                {
                    var hall = new Hall
                    {
                        Name = layout.Name,
                        Format = layout.Format,
                        Rows = layout.Rows,
                        SeatsPerRow = layout.Cols * 2,
                        BlockColumns = layout.Cols,
                        RoomWidth = layout.Width,
                        RoomDepth = layout.Depth,
                        RoomHeight = layout.Height,
                        ScreenWidth = layout.Screen,
                        ScreenHeight = layout.ScreenH,
                        ScreenCurveRadius = layout.Curve,
                        FirstRowDistance = layout.FirstRow,
                        RowPitch = layout.Pitch,
                        RowRise = layout.Rise
                    };
                    venue.Halls.Add(hall);
                    halls.Add(hall);
                }
                cinema.Venues.Add(venue);
            }

            await cinema.SaveChangesAsync(ct);

            var showcase = await catalog.Movies.OrderBy(m => m.Title).Take(5).ToListAsync(ct);
            var slot = DateTime.UtcNow.Date.AddDays(1).AddHours(15);

            var languages = new[]
            {
                ("az", (string?)null), ("en", "az"), ("ru", "az"), ("tr", "en"), ("en", "ru")
            };

            // Spread the films across cinemas and rooms so every hall has something to show.
            foreach (var (movie, index) in showcase.Select((m, i) => (m, i)))
            {
                for (var slotIndex = 0; slotIndex < 3; slotIndex++)
                {
                    var hall = halls[(index * 3 + slotIndex) % halls.Count];
                    var (audio, subtitles) = languages[(index + slotIndex) % languages.Length];

                    cinema.Screenings.Add(new Screening
                    {
                        MovieId = movie.Id,
                        MovieTitle = movie.Title,
                        HallId = hall.Id,
                        Hall = hall.Name,
                        StartsAtUtc = slot.AddDays(slotIndex).AddHours(index * 2),
                        Rows = hall.Rows,
                        SeatsPerRow = hall.SeatsPerRow,
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
