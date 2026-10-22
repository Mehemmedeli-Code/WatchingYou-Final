namespace MovieRental.SharedKernel.Contracts;

public sealed record ChartPoint(string Label, double Value);

public sealed record ChartSeries(
    string Key, string Title, string Unit, string Caption, IReadOnlyList<ChartPoint> Points);

/// <summary>
/// Each module answers for its own data. The host composes the dashboard from these
/// contracts instead of querying another module's tables, so the boundary holds even
/// though the numbers sit side by side on one screen.
/// </summary>
public interface ICatalogAnalytics
{
    Task<ChartSeries> TopRatedAsync(int take, CancellationToken ct = default);
}

public interface IRentalAnalytics
{
    Task<ChartSeries> MostRentedAsync(int take, CancellationToken ct = default);
    Task<ChartSeries> RentalsByGenreAsync(int take, CancellationToken ct = default);
}

public interface ICinemaAnalytics
{
    /// <summary>Ticket money per day, net of refunds, for the last <paramref name="days"/> days.</summary>
    Task<ChartSeries> RevenueByDayAsync(int days, CancellationToken ct = default);

    /// <summary>How full the next screenings are, as a percentage of seats sold.</summary>
    Task<ChartSeries> OccupancyAsync(int take, CancellationToken ct = default);

    /// <summary>Films by tickets sold.</summary>
    Task<ChartSeries> TopFilmsAsync(int take, CancellationToken ct = default);
}

/// <summary>Headline numbers for the admin dashboard's tiles.</summary>
public sealed record CinemaTotals(decimal RevenueThisMonth, int TicketsThisMonth, int UpcomingScreenings, int ActivePromoCodes);

public interface ICinemaTotals
{
    Task<CinemaTotals> TotalsAsync(CancellationToken ct = default);
}
