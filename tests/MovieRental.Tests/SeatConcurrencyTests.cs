using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;

namespace MovieRental.Tests;

/// <summary>
/// The claim this project makes about seat booking is that two people cannot buy the same
/// seat. Everything else — the hold, the code, the QR — assumes it. This is the test that
/// proves it, and it has to run against real SQL Server: the guarantee is a filtered unique
/// index, and an in-memory provider would happily accept both rows and tell us nothing.
///
/// Needs LocalDB. Skip with: dotnet test --project tests/MovieRental.Tests -- --filter-not-trait "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class SeatConcurrencyTests : IAsyncLifetime
{
    private readonly string _database = $"WatchingYouTests_{Guid.NewGuid():N}";
    private string ConnectionString => TestDb.ConnectionString(_database);

    private CinemaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CinemaDbContext>().UseSqlServer(ConnectionString).Options);

    private readonly Guid _screeningId = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        // A throwaway database per run, so a failed test never poisons the next one.
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        // A screening belongs to a real room — the foreign key insists — so the test builds
        // one, the same way the seeder does, rather than pointing at a hall that isn't there.
        var hall = new Hall { Name = "Hall B", Rows = 8, SeatsPerRow = 12 };
        db.Venues.Add(new Venue { Name = "Test Cinema", Halls = [hall] });

        db.Screenings.Add(new Screening
        {
            Id = _screeningId,
            MovieId = Guid.NewGuid(),
            MovieTitle = "Blue Hour",
            HallId = hall.Id,
            Hall = hall.Name,
            StartsAtUtc = DateTime.UtcNow.AddDays(1),
            Rows = 8,
            SeatsPerRow = 12,
            SeatPrice = 8.50m,
            AudioLanguage = "en",
            SubtitleLanguage = "az"
        });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private SeatPayment PaymentFor(Guid userId, int row, int number) => new()
    {
        ScreeningId = _screeningId,
        UserId = userId,
        Reference = $"WY-{Guid.NewGuid():N}"[..9],
        Amount = 8.50m,
        Brand = CardBrand.Visa,
        Last4 = "4242",
        CardHolder = "Test Holder",
        CodeHash = "hash",
        Salt = "salt",
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
        Seats =
        [
            new SeatBooking { ScreeningId = _screeningId, UserId = userId, Row = row, Number = number, PricePaid = 8.50m }
        ]
    };

    [Fact]
    public async Task Two_people_cannot_hold_the_same_seat()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        // Separate contexts, started together: this is the race as it happens in production,
        // where both requests pass the availability read before either one writes.
        async Task<bool> TryBook(Guid userId)
        {
            await using var db = NewContext();
            db.SeatPayments.Add(PaymentFor(userId, row: 3, number: 5));
            try
            {
                await db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(TryBook(alice), TryBook(bob));

        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Equal(1, outcomes.Count(success => !success));

        await using var check = NewContext();
        var held = await check.SeatBookings.CountAsync(s => s.Row == 3 && s.Number == 5);
        Assert.Equal(1, held);
    }

    [Fact]
    public async Task Different_seats_in_the_same_row_do_not_collide()
    {
        // The index must be tight enough to stop a clash and loose enough to sell a row.
        async Task Book(int number)
        {
            await using var db = NewContext();
            db.SeatPayments.Add(PaymentFor(Guid.NewGuid(), row: 6, number: number));
            await db.SaveChangesAsync();
        }

        await Task.WhenAll(Book(1), Book(2), Book(3));

        await using var check = NewContext();
        Assert.Equal(3, await check.SeatBookings.CountAsync(s => s.Row == 6));
    }

    [Fact]
    public async Task A_released_seat_can_be_sold_again()
    {
        // Releasing a seat soft-deletes the booking. What makes that work is the index
        // filter, [IsDeleted] = 0: the released row drops out of the unique constraint, so
        // the seat can be sold again while the abandoned attempt stays on record. Without
        // the filter, one expired checkout would take a seat off sale for ever.
        var first = PaymentFor(Guid.NewGuid(), row: 2, number: 9);

        await using (var db = NewContext())
        {
            db.SeatPayments.Add(first);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var stale = await db.SeatPayments.Include(p => p.Seats).SingleAsync(p => p.Id == first.Id);
            db.SeatBookings.RemoveRange(stale.Seats);
            stale.Status = PaymentStatus.Expired;
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.SeatPayments.Add(PaymentFor(Guid.NewGuid(), row: 2, number: 9));
            await db.SaveChangesAsync();      // must not throw
        }

        await using var check = NewContext();
        Assert.Equal(1, await check.SeatBookings.CountAsync(s => s.Row == 2 && s.Number == 9));
    }
}
