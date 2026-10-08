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
    private const string SchemaStamp = "2026-09-27-backoffice";

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

        await AddColumnsInPlaceAsync(scope.ServiceProvider, ct);
        await SeedAsync(scope.ServiceProvider, ct);
        await WriteStampAsync(scope.ServiceProvider, ct);
    }

    /// <summary>
    /// Nullable columns added after the last stamp. Bumping the stamp would drop the database
    /// and every sale, shift and account in it for the sake of a column that can simply be
    /// added, so purely additive nullable changes go here instead. Idempotent: each ALTER runs
    /// only when its column is missing, so a fresh database (already built with them) skips it.
    /// </summary>
    private static async Task AddColumnsInPlaceAsync(IServiceProvider services, CancellationToken ct)
    {
        var cinema = services.GetRequiredService<CinemaDbContext>();
        await cinema.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('cinema.TicketTypes', 'NameAz') IS NULL ALTER TABLE cinema.TicketTypes ADD NameAz nvarchar(40) NULL;
            IF COL_LENGTH('cinema.TicketTypes', 'NameRu') IS NULL ALTER TABLE cinema.TicketTypes ADD NameRu nvarchar(40) NULL;
            IF COL_LENGTH('cinema.TicketTypes', 'NameTr') IS NULL ALTER TABLE cinema.TicketTypes ADD NameTr nvarchar(40) NULL;
            """, ct);

        // A whole new table in an existing schema is additive too: the schema already has tables,
        // so the create-tables step above skips it. Same shape EF would make.
        var rentals = services.GetRequiredService<RentalsDbContext>();
        await rentals.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('rentals.Subscriptions', 'U') IS NULL
            BEGIN
                CREATE TABLE rentals.Subscriptions (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_Subscriptions PRIMARY KEY,
                    UserId uniqueidentifier NOT NULL,
                    [Plan] nvarchar(40) NOT NULL,
                    StartsAtUtc datetime2 NOT NULL,
                    EndsAtUtc datetime2 NOT NULL,
                    Amount decimal(10,2) NOT NULL,
                    Currency nvarchar(3) NOT NULL,
                    CardBrand nvarchar(20) NOT NULL,
                    CardLast4 nvarchar(4) NOT NULL,
                    ReminderSentAtUtc datetime2 NULL,
                    CreatedAtUtc datetime2 NOT NULL,
                    UpdatedAtUtc datetime2 NULL);
                CREATE INDEX IX_Subscriptions_UserId_EndsAtUtc ON rentals.Subscriptions (UserId, EndsAtUtc);
            END
            IF COL_LENGTH('rentals.Subscriptions', 'ReminderSentAtUtc') IS NULL
                ALTER TABLE rentals.Subscriptions ADD ReminderSentAtUtc datetime2 NULL;
            """, ct);
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

        // Added after the first three, so a database seeded before the back office existed
        // still gets its till account without being rebuilt.
        if (!await identity.Users.AnyAsync(u => u.Email == "kassa@reelandrow.test", ct))
        {
            identity.Users.Add(new AppUser
            {
                Email = "kassa@reelandrow.test", FullName = "Leyla Karimova",
                PasswordHash = hasher.Hash("Kassa1234"), IsEmailConfirmed = true,
                Roles = AppRoles.Cashier
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

        // Two codes to try the checkout with. Real ones are made on the admin page.
        if (!await cinema.PromoCodes.AnyAsync(ct))
        {
            cinema.PromoCodes.AddRange(
                new PromoCode { Code = "WELCOME10", Description = "10% off — demo code", PercentOff = 10m },
                new PromoCode { Code = "KINO5", Description = "5 AZN off bookings of 15 AZN or more", AmountOff = 5m, MinSubtotal = 15m, MaxRedemptions = 100 });
            await cinema.SaveChangesAsync(ct);
        }

        // Back office: the price bands at the box office, a bar menu to sell from, and
        // distributor terms for the films on the schedule.
        if (!await cinema.TicketTypes.AnyAsync(ct))
        {
            cinema.TicketTypes.AddRange(
                new TicketType { Name = "Adult", PercentOfBase = 100m, SortOrder = 1 },
                new TicketType { Name = "Student", PercentOfBase = 80m, SortOrder = 2 },
                new TicketType { Name = "Child", PercentOfBase = 60m, SortOrder = 3 },
                new TicketType { Name = "Senior", PercentOfBase = 70m, SortOrder = 4 });
            await cinema.SaveChangesAsync(ct);

            // The seeded schedule starts tomorrow, which leaves the box office nothing to sell
            // on the day you first open it. A few shows later today fix that.
            var baku = TimeSpan.FromHours(4);
            var todayLocal = (DateTime.UtcNow + baku).Date;
            var rooms = await cinema.Halls.AsNoTracking().OrderBy(h => h.Name).Take(3).ToListAsync(ct);
            var films = await catalog.Movies.AsNoTracking().OrderBy(m => m.Title).Take(3).ToListAsync(ct);
            var evening = new[] { 19, 21, 23 };

            for (var i = 0; i < Math.Min(rooms.Count, films.Count); i++)
            {
                var startUtc = DateTime.SpecifyKind(todayLocal.AddHours(evening[i]) - baku, DateTimeKind.Utc);
                if (startUtc <= DateTime.UtcNow.AddMinutes(30)) startUtc = DateTime.UtcNow.AddHours(1 + i);

                cinema.Screenings.Add(new Screening
                {
                    MovieId = films[i].Id, MovieTitle = films[i].Title,
                    HallId = rooms[i].Id, Hall = rooms[i].Name, StartsAtUtc = startUtc,
                    Rows = rooms[i].Rows, SeatsPerRow = rooms[i].SeatsPerRow,
                    SeatPrice = 10m, AudioLanguage = "az", SubtitleLanguage = "en"
                });
            }
            await cinema.SaveChangesAsync(ct);
        }

        // The standard tariffs in the other three languages. Only fills a translation nobody has
        // entered yet, so a name the manager changed on the Prices page is never overwritten.
        var standardNames = new Dictionary<string, (string Az, string Ru, string Tr)>
        {
            ["Adult"] = ("Böyük", "Взрослый", "Tam"),
            ["Student"] = ("Tələbə", "Студенческий", "Öğrenci"),
            ["Child"] = ("Uşaq", "Детский", "Çocuk"),
            ["Senior"] = ("Təqaüdçü", "Пенсионный", "Emekli"),
        };
        var untranslated = await cinema.TicketTypes
            .Where(t => standardNames.Keys.Contains(t.Name) && (t.NameAz == null || t.NameRu == null || t.NameTr == null))
            .ToListAsync(ct);
        foreach (var type in untranslated)
        {
            var names = standardNames[type.Name];
            type.NameAz ??= names.Az;
            type.NameRu ??= names.Ru;
            type.NameTr ??= names.Tr;
        }
        if (untranslated.Count > 0) await cinema.SaveChangesAsync(ct);

        if (!await cinema.ConcessionItems.AnyAsync(ct))
        {
            cinema.ConcessionItems.AddRange(
                new ConcessionItem { Name = "Popcorn S", Category = ConcessionCategory.Popcorn, Price = 4m, CostPrice = 0.9m, Stock = 200 },
                new ConcessionItem { Name = "Popcorn M", Category = ConcessionCategory.Popcorn, Price = 6m, CostPrice = 1.3m, Stock = 200 },
                new ConcessionItem { Name = "Popcorn L", Category = ConcessionCategory.Popcorn, Price = 8m, CostPrice = 1.7m, Stock = 150 },
                new ConcessionItem { Name = "Caramel popcorn", Category = ConcessionCategory.Popcorn, Price = 7m, CostPrice = 1.9m, Stock = 80 },
                new ConcessionItem { Name = "Coca-Cola 0.5", Category = ConcessionCategory.Drinks, Price = 3m, CostPrice = 1.1m, Stock = 240 },
                new ConcessionItem { Name = "Fanta 0.5", Category = ConcessionCategory.Drinks, Price = 3m, CostPrice = 1.1m, Stock = 120 },
                new ConcessionItem { Name = "Water 0.5", Category = ConcessionCategory.Drinks, Price = 1.5m, CostPrice = 0.35m, Stock = 300 },
                new ConcessionItem { Name = "Iced tea", Category = ConcessionCategory.Drinks, Price = 3.5m, CostPrice = 1.2m, Stock = 8, LowStockThreshold = 12 },
                new ConcessionItem { Name = "Nachos", Category = ConcessionCategory.Snacks, Price = 5.5m, CostPrice = 1.8m, Stock = 60 },
                new ConcessionItem { Name = "Hot dog", Category = ConcessionCategory.Snacks, Price = 4.5m, CostPrice = 1.6m, Stock = 45 },
                new ConcessionItem { Name = "M&M's", Category = ConcessionCategory.Snacks, Price = 3m, CostPrice = 1.4m, Stock = 5, LowStockThreshold = 10 },
                new ConcessionItem { Name = "Combo: Popcorn M + Cola", Category = ConcessionCategory.Combo, Price = 8m, CostPrice = 2.4m, Stock = 150 },
                new ConcessionItem { Name = "Combo for two: Popcorn L + 2 Cola", Category = ConcessionCategory.Combo, Price = 12m, CostPrice = 3.9m, Stock = 100 });
            await cinema.SaveChangesAsync(ct);
        }

        if (!await cinema.FilmDeals.AnyAsync(ct))
        {
            var distributors = new[] { ("Caspian Film Distribution", 50m), ("Kino Film Baku", 55m), ("Nord Pictures", 45m) };
            var playing = await cinema.Screenings.AsNoTracking()
                .Select(s => new { s.MovieId, s.MovieTitle }).Distinct().ToListAsync(ct);
            foreach (var (film, index) in playing.OrderBy(f => f.MovieTitle).Select((f, i) => (f, i)))
            {
                var (name, share) = distributors[index % distributors.Length];
                cinema.FilmDeals.Add(new FilmDeal
                {
                    MovieId = film.MovieId, MovieTitle = film.MovieTitle, Distributor = name, SharePercent = share
                });
            }
            await cinema.SaveChangesAsync(ct);
        }
    }
}
