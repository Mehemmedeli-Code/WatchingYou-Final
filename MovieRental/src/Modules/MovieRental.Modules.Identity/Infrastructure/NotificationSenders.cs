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
    public string FromName { get; set; } = "Reel & Row";
}

public sealed class SmsOptions
{
    public const string SectionName = "Notifications:Sms";

    /// <summary>"Twilio" or "Console".</summary>
    public string Provider { get; set; } = "Console";
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
}

/// <summary>Development transport: the code goes to the log, nothing leaves the machine.</summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailRequest request, CancellationToken ct = default)
    {
        logger.LogWarning("E-MAIL to {To} | {Subject}\n{Body}", request.To, request.Subject,
            request.PlainTextBody ?? request.HtmlBody);
        return Task.CompletedTask;
    }
}

public sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public Task SendAsync(SmsRequest request, CancellationToken ct = default)
    {
        logger.LogWarning("SMS to {Phone} | {Text}", request.PhoneNumber, request.Text);
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

/// <summary>Real delivery through Twilio's REST API — one form POST, no SDK.</summary>
public sealed class TwilioSmsSender(
    IHttpClientFactory factory, IOptions<SmsOptions> options, ILogger<TwilioSmsSender> logger) : ISmsSender
{
    private readonly SmsOptions _options = options.Value;

    public async Task SendAsync(SmsRequest request, CancellationToken ct = default)
    {
        var client = factory.CreateClient(nameof(TwilioSmsSender));
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = request.PhoneNumber,
            ["From"] = _options.FromNumber,
            ["Body"] = request.Text
        });

        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json") { Content = content };
        message.Headers.Add("Authorization", $"Basic {credentials}");

        var response = await client.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("SMS to {Phone} failed: {Status} {Body}", request.PhoneNumber, response.StatusCode, body);
            throw new InvalidOperationException("The SMS could not be sent. Check Notifications:Sms settings.");
        }

        logger.LogInformation("SMS delivered to {Phone}", request.PhoneNumber);
    }
}
