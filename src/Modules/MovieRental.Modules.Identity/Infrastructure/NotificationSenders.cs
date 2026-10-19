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

        // Gmail answers a burst with "421/450, try again later", and a connection can drop. Two
        // more tries, a few seconds apart, before a code is given up on. A refusal that will not
        // change (wrong password, the day's sending quota used up) fails at once.
        // ponytail: retried in the request; a provider made for volume (Brevo, SES) is the fix
        // for Gmail's ~500 messages a day, and only needs its SMTP settings in user secrets.
        TimeSpan[] waits = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];
        for (var attempt = 0; ; attempt++)
        {
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.UseStartTls,
                Credentials = new NetworkCredential(_options.UserName, _options.Password)
            };
            // A try that failed half-way has read the attachments (the PDF ticket) to the end.
            foreach (var attachment in message.Attachments) attachment.ContentStream.Position = 0;
            try
            {
                await client.SendMailAsync(message, ct);
                logger.LogInformation("E-mail delivered to {To} | {Subject}", request.To, request.Subject);
                return;
            }
            catch (Exception ex) when (attempt < waits.Length && IsTransient(ex) && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "E-mail to {To} failed for now; trying again in {Wait}", request.To, waits[attempt]);
                await Task.Delay(waits[attempt], ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "E-mail to {To} failed", request.To);
                throw new InvalidOperationException("The e-mail could not be sent. Check Notifications:Email settings.", ex);
            }
        }
    }

    /// <summary>A busy or dropped server, not a refusal: 4xx replies and broken connections.</summary>
    internal static bool IsTransient(Exception ex) => ex switch
    {
        SmtpException smtp when (int)smtp.StatusCode is >= 400 and < 500 => true,
        SmtpException { StatusCode: SmtpStatusCode.GeneralFailure } => true,   // no connection made
        SmtpException { InnerException: IOException or System.Net.Sockets.SocketException } => true,
        IOException or System.Net.Sockets.SocketException => true,
        _ => false
    };
}
