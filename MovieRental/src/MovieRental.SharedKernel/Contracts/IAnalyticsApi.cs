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
