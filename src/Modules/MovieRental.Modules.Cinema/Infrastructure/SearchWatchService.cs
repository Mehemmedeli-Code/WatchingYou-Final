using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Cinema.Features;
using MovieRental.Modules.Cinema.Persistence;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>
/// SearchingYou: calls off a search with tickets still to buy once its screening no longer has a
/// seat for everyone in the group (see Searches.CallOffShortAsync). The owner and the people who
/// joined then read "No tickets were bought, the meet-up is off" on the page.
/// </summary>
internal sealed class SearchWatchService(IServiceScopeFactory scopes, ILogger<SearchWatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();
                // Bought first: a group whose seats are all paid for is never called off.
                await Searches.MarkBoughtAsync(db, DateTime.UtcNow, stoppingToken);
                var off = await Searches.CallOffShortAsync(db, DateTime.UtcNow, stoppingToken);
                if (off > 0) logger.LogInformation("Called off {Count} search(es): too few seats left for the group.", off);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "SearchingYou sweep failed."); }
        }
    }
}
