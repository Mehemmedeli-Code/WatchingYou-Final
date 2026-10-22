using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Rentals.Infrastructure;

/// <summary>
/// Feature 8 — due-date alerts. A hosted service rather than a queue: the monolith already
/// owns the data and the volume is small. The notified flags make the pass idempotent, so
/// a restart mid-run never double-mails a customer.
/// </summary>
public sealed class DueDateNotificationService(
    IServiceScopeFactory scopeFactory, ILogger<DueDateNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan NoticeWindow = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunPassAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Due-date notification pass failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RentalsDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var now = DateTime.UtcNow;
        var horizon = now.Add(NoticeWindow);

        var dueSoon = await db.Rentals
            .Where(r => r.ReturnedAtUtc == null && !r.DueSoonNotified && r.DueAtUtc > now && r.DueAtUtc <= horizon)
            .ToListAsync(ct);

        var overdue = await db.Rentals
            .Where(r => r.ReturnedAtUtc == null && !r.OverdueNotified && r.DueAtUtc <= now)
            .ToListAsync(ct);

        foreach (var rental in dueSoon)
        {
            var contact = await users.GetContactAsync(rental.UserId, ct);
            if (contact is null) continue;

            await email.SendAsync(new EmailRequest(contact.Email, $"{rental.MovieTitle} is due tomorrow",
                $"<p>Hi {contact.FullName}, return it by {rental.DueAtUtc:dd MMM HH:mm} UTC or extend it from your rentals page.</p>"), ct);
            rental.DueSoonNotified = true;
        }

        foreach (var rental in overdue)
        {
            var contact = await users.GetContactAsync(rental.UserId, ct);
            if (contact is null) continue;

            await email.SendAsync(new EmailRequest(contact.Email, $"{rental.MovieTitle} is overdue",
                "<p>A late fee is now accruing each day. Return the title to stop it.</p>"), ct);
            rental.OverdueNotified = true;
        }

        if (dueSoon.Count + overdue.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Notified {DueSoon} due-soon and {Overdue} overdue rentals.", dueSoon.Count, overdue.Count);
        }
    }
}
