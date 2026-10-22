using Microsoft.Extensions.Logging;
using MovieRental.SharedKernel.Contracts;

namespace MovieRental.Modules.Identity.Infrastructure;

/// <summary>Development transport. Swap for SendGrid/Twilio in Program.cs — nothing else
/// in the solution knows which implementation is registered.</summary>
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
