using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// What the checks outside the site found. tools\db-check.ps1 (daily, after the backup) and
/// tools\api-check.ps1 (nightly, and before every deploy) each leave a JSON report in one
/// folder; the admin panel shows them, and the owner is e-mailed when one finds a problem —
/// so a broken rule does not wait in a log file nobody opens.
/// </summary>
/// <param name="Problems">Each problem in a line; empty when all is well.</param>
public sealed record HealthReport(string Name, DateTime AtUtc, bool Ok, string? Summary, string[] Problems);

public static class HealthReports
{
    /// <summary>Health:ReportFolder, or the checks folder beside the backups.</summary>
    public static string Folder(IConfiguration configuration) =>
        configuration["Health:ReportFolder"] is { Length: > 0 } folder
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WatchingYouBackups", "checks");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The reports there now, newest first. A file that cannot be read is skipped.</summary>
    public static IReadOnlyList<HealthReport> Read(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var reports = new List<HealthReport>();
        foreach (var file in Directory.EnumerateFiles(folder, "*-check.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<HealthReport>(File.ReadAllText(file), Json) is { } report) reports.Add(report);
            }
            catch (Exception ex) when (ex is IOException or JsonException) { /* half written: read it next time */ }
        }
        return [.. reports.OrderByDescending(r => r.AtUtc)];
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/health", (IConfiguration configuration) => TypedResults.Ok(Read(Folder(configuration))))
            .WithName("GetHealthReports").WithTags("Analytics").RequireAuthorization(AppRoles.Admin)
            .WithSummary("The latest database and API checks")
            .WithDescription("One report per check (db: business rules and links between modules; api: the full API check), " +
                             "newest first, each with its problems. Empty where the checks have not run on this machine.");
}

/// <summary>E-mails the owner when a check report finds problems, once per report.</summary>
internal sealed class HealthMailer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<HealthMailer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var folder = HealthReports.Folder(configuration);
        var mailedFile = Path.Combine(folder, "mailed.json");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                var mailed = File.Exists(mailedFile)
                    ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(await File.ReadAllTextAsync(mailedFile, stoppingToken)) ?? []
                    : [];
                var fresh = HealthReports.Read(folder)
                    .Where(r => !r.Ok && (!mailed.TryGetValue(r.Name, out var at) || r.AtUtc > at)).ToList();
                if (fresh.Count == 0) continue;

                await using var scope = scopes.CreateAsyncScope();
                var owners = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AsNoTracking()
                    .Where(u => u.Roles.Contains(AppRoles.SuperAdmin) && !u.IsSuspended)
                    .Select(u => u.Email).ToListAsync(stoppingToken);
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                foreach (var report in fresh)
                {
                    var lines = string.Join("", report.Problems.Take(30).Select(p => $"<li>{WebUtility.HtmlEncode(p)}</li>"));
                    var body = $"<p>The {WebUtility.HtmlEncode(report.Name)} check of {report.AtUtc:yyyy-MM-dd HH:mm} UTC found " +
                               $"{report.Problems.Length} problem(s):</p><ul>{lines}</ul>" +
                               "<p>The admin panel shows the same under Health.</p>";
                    foreach (var owner in owners)
                        await email.SendAsync(new EmailRequest(owner, $"WatchingYou: the {report.Name} check found problems", body), stoppingToken);
                    mailed[report.Name] = report.AtUtc;
                    logger.LogWarning("The {Check} check found {Count} problem(s); the owner was e-mailed.", report.Name, report.Problems.Length);
                }
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(mailedFile, JsonSerializer.Serialize(mailed), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Reading the check reports failed."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
