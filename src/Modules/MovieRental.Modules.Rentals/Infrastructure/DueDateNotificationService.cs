using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Rentals.Domain;
using MovieRental.Modules.Rentals.Persistence;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Rentals.Infrastructure;

/// <summary>
/// The reminder e-mails, each with a button straight to the page where the answer is given:
///
///  • a rental's paid three days are over → "+3 days, return it, or Watching PRO?" → /rentals
///  • Watching PRO ends in three days → "add another month?" → /pro
///
/// A hosted service rather than a queue: the monolith already owns the data and the volume is
/// small. The sent flags make every pass idempotent, so a restart never mails anyone twice.
/// </summary>
public sealed class DueDateNotificationService(
    IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<DueDateNotificationService> logger)
    : BackgroundService
{
    // Often enough that "your three days are over" arrives when they are, not hours later.
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ProNotice = TimeSpan.FromDays(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunPassAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Reminder e-mail pass failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private string Site => (configuration["App:PublicUrl"] ?? "https://localhost:7139").TrimEnd('/');

    private async Task RunPassAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RentalsDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var now = DateTime.UtcNow;

        // Rentals whose paid three days are over and who have not been asked yet.
        var ended = await db.Rentals
            .Where(r => r.ReturnedAtUtc == null && !r.OverdueNotified && r.DueAtUtc <= now)
            .ToListAsync(ct);

        foreach (var rental in ended)
        {
            var contact = await users.GetContactAsync(rental.UserId, ct);
            if (contact is null) continue;

            var title = WebUtility.HtmlEncode(rental.MovieTitle);
            await email.SendAsync(new EmailRequest(contact.Email,
                $"{rental.MovieTitle} filminin icarə müddəti başa çatdı",
                Letter(contact.FullName,
                    $"<strong>{title}</strong> filmi üçün 3 günlük icarə müddətiniz başa çatmışdır. " +
                    "Film kitabxananızda saxlanılır və növbəti seçiminizi gözləyir:" +
                    "<br><br>• <strong>İcarəni 3 gün uzatmaq</strong> — $0.50" +
                    "<br>• <strong>Filmi qaytarmaq</strong> — əlavə ödəniş tələb olunmur" +
                    "<br>• <strong>Watching PRO</strong> — ayda $5, bütün filmlərə limitsiz giriş" +
                    "<br><br>Seçiminizi edənə qədər hesabınızdan heç bir ödəniş tutulmayacaq.",
                    $"Your 3-day rental of <strong>{title}</strong> has ended. The film remains in your library until you decide: " +
                    "extend it by 3 days for $0.50, return it at no charge, or subscribe to Watching PRO for $5 a month. " +
                    "No charge will be made until you choose.",
                    "Seçimi et", $"{Site}/rentals")), ct);

            rental.OverdueNotified = true;
        }

        // Watching PRO running out within three days, not yet reminded. Only the latest paid
        // month counts: someone who already paid ahead is not running out.
        var horizon = now.Add(ProNotice);
        var running = await db.Subscriptions.Where(s => s.EndsAtUtc > now).ToListAsync(ct);
        var endingSoon = running
            .GroupBy(s => s.UserId)
            .Select(g => g.MaxBy(s => s.EndsAtUtc)!)
            .Where(s => s.EndsAtUtc <= horizon && s.ReminderSentAtUtc == null)
            .ToList();

        foreach (var sub in endingSoon)
        {
            var contact = await users.GetContactAsync(sub.UserId, ct);
            if (contact is null) continue;

            var days = Math.Max(1, (int)Math.Ceiling((sub.EndsAtUtc - now).TotalDays));
            await email.SendAsync(new EmailRequest(contact.Email,
                $"{RentalPricing.PlanName} abunəliyinizin bitməsinə {days} gün qalıb",
                Letter(contact.FullName,
                    $"{RentalPricing.PlanName} abunəliyiniz <strong>{sub.EndsAtUtc:dd.MM.yyyy}</strong> tarixində başa çatır " +
                    $"(qalan müddət: {days} gün). Bütün filmlərə limitsiz girişi davam etdirmək üçün abunəliyinizi " +
                    $"daha bir ay uzada bilərsiniz — ${RentalPricing.MonthlyPrice:0.00}." +
                    "<br><br>Abunəlik avtomatik yenilənmir: heç bir addım atmasanız, hesabınızdan ödəniş tutulmayacaq.",
                    $"Your {RentalPricing.PlanName} subscription ends on <strong>{sub.EndsAtUtc:dd MMM yyyy}</strong> ({days} days left). " +
                    $"You may extend it by one month for ${RentalPricing.MonthlyPrice:0.00}. It does not renew automatically, " +
                    "so no charge will be made unless you choose to renew.",
                    "Abunəliyi uzat", $"{Site}/pro")), ct);

            sub.ReminderSentAtUtc = now;
        }

        if (ended.Count + endingSoon.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Reminders sent: {Rentals} ended rentals, {Pro} Watching PRO renewals.", ended.Count, endingSoon.Count);
        }
    }

    /// <summary>
    /// A formal letter: Azerbaijani with one button, signed by the team, and the same text in
    /// English below a rule for anyone who reads it better that way.
    /// </summary>
    private static string Letter(string name, string az, string en, string button, string url) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;color:#111">
          <p>Hörmətli {WebUtility.HtmlEncode(name)},</p>
          <p style="line-height:1.6">{az}</p>
          <p style="margin:24px 0">
            <a href="{url}" style="background:#16C768;color:#03140A;padding:12px 24px;border-radius:8px;text-decoration:none;font-weight:bold;display:inline-block">{button}</a>
          </p>
          <p style="line-height:1.6">Hörmətlə,<br>WatchingYou komandası</p>
          <hr style="border:none;border-top:1px solid #ddd;margin:28px 0 16px">
          <p style="color:#666;font-size:12px;line-height:1.5"><strong>In English:</strong> {en}</p>
        </div>
        """;
}
