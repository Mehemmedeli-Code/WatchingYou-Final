using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Identity.Infrastructure;

public sealed class EmailOptions
{
    public const string SectionName = "Notifications:Email";

    /// <summary>"Smtp" to send for real, "Console" to log instead. Anything else is rejected
    /// at startup rather than silently doing nothing.</summary>
    public string Provider { get; set; } = "Console";
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "WatchingYou";
}

/// <summary>Development transport: the code goes to the log, nothing leaves the machine.</summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailRequest request, CancellationToken ct = default)
    {
        logger.LogWarning("E-MAIL to {To} | {Subject}\n{Body}", request.To, request.Subject,
            request.PlainTextBody ?? request.HtmlBody);
        foreach (var file in request.Attachments ?? [])
            logger.LogWarning("  attachment: {Name} ({Type}, {Size:N0} bytes)", file.FileName, file.ContentType, file.Content.Length);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Real delivery over SMTP. A failure throws rather than returning quietly — a verification
/// code that was never sent must not look like one that was.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailRequest request, CancellationToken ct = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = request.Subject,
            Body = request.HtmlBody,
            IsBodyHtml = true
        };
        message.To.Add(request.To);

        foreach (var file in request.Attachments ?? [])
            message.Attachments.Add(new Attachment(new MemoryStream(file.Content), file.FileName, file.ContentType));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseStartTls,
            Credentials = new NetworkCredential(_options.UserName, _options.Password)
        };

        try
        {
            await client.SendMailAsync(message, ct);
            logger.LogInformation("E-mail delivered to {To} | {Subject}", request.To, request.Subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "E-mail to {To} failed", request.To);
            throw new InvalidOperationException("The e-mail could not be sent. Check Notifications:Email settings.", ex);
        }
    }
}
