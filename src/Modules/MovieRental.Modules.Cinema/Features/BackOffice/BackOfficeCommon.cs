using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;

namespace MovieRental.Modules.Cinema.Features;

/// <summary>Bits every back-office slice needs: the local business day and the cashier's
/// open shift.</summary>
internal static class BackOfficeClock
{
    /// <summary>A cinema's day is cut on local time. A ticket sold at 00:30 for the late show
    /// belongs to the night the manager thinks it does, not to the UTC date.</summary>
    public static readonly TimeSpan Baku = TimeSpan.FromHours(4);

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow + Baku);

    public static DateTime StartUtc(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue) - Baku, DateTimeKind.Utc);

    public static DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(utc + Baku);

    /// <summary>Parses yyyy-MM-dd, falling back when absent or malformed.</summary>
    public static DateOnly Parse(string? text, DateOnly fallback) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day : fallback;

    /// <summary>A range of local days as a half-open UTC interval, clamped to a sane length.</summary>
    public static (DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtc) Range(string? from, string? to, int defaultDays = 7)
    {
        var last = Parse(to, Today);
        var first = Parse(from, last.AddDays(-(defaultDays - 1)));
        if (first > last) (first, last) = (last, first);
        if (last.DayNumber - first.DayNumber > 92) first = last.AddDays(-92);
        return (first, last, StartUtc(first), StartUtc(last.AddDays(1)));
    }
}

internal static class BackOfficeReference
{
    /// <summary>Bar receipts get their own prefix, so a reference read out at the counter
    /// says at once whether it is a ticket (WY-) or a receipt (BAR-).</summary>
    public static string ForBar()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return $"BAR-{new string(chars)}";
    }
}

internal static class Shifts
{
    public static Task<CashShift?> OpenForAsync(CinemaDbContext db, Guid cashierId, CancellationToken ct) =>
        db.CashShifts.FirstOrDefaultAsync(s => s.CashierId == cashierId && s.ClosedAtUtc == null, ct);
}
