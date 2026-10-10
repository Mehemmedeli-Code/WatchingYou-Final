using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Cinema.Domain;
using MovieRental.Modules.Cinema.Persistence;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>
/// Sweeps up checkouts that were never confirmed.
///
/// Without this, an abandoned payment blocks its seats until somebody else happens to start
/// a checkout on the same screening — which might be never. Removing a booking soft-deletes
/// it, and the unique index on (screening, row, number) is filtered to [IsDeleted] = 0, so
/// the row leaves the constraint and the seat goes back on sale while the history survives.
/// </summary>
internal sealed class HoldExpiryService(IServiceScopeFactory scopes, ILogger<HoldExpiryService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A minute is short enough that an abandoned basket frees up while the customer is
        // still deciding, and cheap enough to run forever.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CinemaDbContext>();

                var stripe = scope.ServiceProvider.GetRequiredService<IStripeGateway>();
                var finalizer = scope.ServiceProvider.GetRequiredService<BookingFinalizer>();

                var stale = await db.SeatPayments
                    .Include(p => p.Seats)
                    .Include(p => p.Screening!).ThenInclude(s => s.HallRoom!).ThenInclude(h => h.Venue)
                    .Where(p => p.Status == PaymentStatus.AwaitingCode && p.ExpiresAtUtc < DateTime.UtcNow)
                    .ToListAsync(stoppingToken);

                if (stale.Count == 0) continue;

                var released = 0;
                foreach (var payment in stale)
                {
                    // A Stripe customer who paid but never came back to the site has no ticket
                    // yet. Ask Stripe before expiring: paid means confirm, not keep the money.
                    if (payment.Provider == PaymentProvider.Stripe && stripe.Enabled && !string.IsNullOrEmpty(payment.ExternalSessionId))
                    {
                        StripeSessionStatus status;
                        try { status = await stripe.GetSessionAsync(payment.ExternalSessionId, stoppingToken); }
                        catch (Exception ex) when (ex is StripeException or HttpRequestException)
                        {
                            logger.LogWarning(ex, "Could not check Stripe session for {Reference}; will retry.", payment.Reference);
                            continue;
                        }

                        if (status.Paid && status.ClientReferenceId == payment.Id.ToString()
                            && status.AmountTotal == stripe.ToMinorUnits(payment.Amount))
                        {
                            payment.ExternalPaymentId = status.PaymentIntentId;
                            await finalizer.ConfirmAsync(payment, stoppingToken);
                            logger.LogInformation("Confirmed Stripe booking {Reference} the customer never returned for.", payment.Reference);
                            continue;
                        }
                    }

                    // A confirm may have claimed this payment since it was read (see
                    // BookingFinalizer); only release seats whose payment this sweep claimed.
                    var claimed = await db.SeatPayments.IgnoreQueryFilters()
                        .Where(p => p.Id == payment.Id && p.Status == PaymentStatus.AwaitingCode)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Expired), stoppingToken);
                    if (claimed == 0)
                    {
                        db.Entry(payment).State = EntityState.Detached;
                        continue;
                    }

                    db.SeatBookings.RemoveRange(payment.Seats);
                    payment.Status = PaymentStatus.Expired;
                    released++;
                }

                await db.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Released {Count} expired seat hold(s).", released);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Never let a sweep failure take the host down; it will try again next tick.
                logger.LogError(ex, "Seat hold sweep failed.");
            }
        }
    }
}
