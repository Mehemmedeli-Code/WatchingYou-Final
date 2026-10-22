using MovieRental.Modules.Cinema.Features;

namespace MovieRental.Tests;

public class BulkScheduleTests
{
    private static BulkScheduleCommand Command(string first, int days, string[] times, int[]? weekdays = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), first, days, times, weekdays, 9m, "az", null);

    [Fact]
    public void Local_Baku_times_become_utc_four_hours_earlier()
    {
        var slots = BulkScheduleHandler.Slots(Command("2026-10-05", 1, ["19:30"])).ToList();

        Assert.Single(slots);
        Assert.Equal(new DateTime(2026, 10, 5, 15, 30, 0, DateTimeKind.Utc), slots[0]);
        Assert.Equal(DateTimeKind.Utc, slots[0].Kind);
    }

    [Fact]
    public void Every_day_times_every_time()
    {
        var slots = BulkScheduleHandler.Slots(Command("2026-10-05", 7, ["16:00", "19:30", "16:00"])).ToList();
        Assert.Equal(14, slots.Count);                    // duplicate time counted once
    }

    [Fact]
    public void Weekday_filter_keeps_only_those_days()
    {
        // 5 Oct 2026 is a Monday. Fridays and Saturdays over two weeks: 4 days.
        var slots = BulkScheduleHandler.Slots(Command("2026-10-05", 14, ["20:00"], [5, 6])).ToList();

        Assert.Equal(4, slots.Count);
        Assert.All(slots, s => Assert.True(s.AddHours(4).DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday));
    }

    [Fact]
    public void A_bad_date_yields_nothing() =>
        Assert.Empty(BulkScheduleHandler.Slots(Command("05/10/2026", 3, ["20:00"])));
}
