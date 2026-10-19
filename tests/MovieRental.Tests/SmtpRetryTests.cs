using System.Net.Mail;
using System.Net.Sockets;
using MovieRental.Modules.Identity.Infrastructure;

namespace MovieRental.Tests;

/// <summary>
/// Which SMTP failures are worth a second try. Gmail's "busy, try later" (4xx) and a dropped
/// connection are; a wrong password or the day's quota used up (5xx) never get better by
/// asking again, and retrying them only makes the person wait longer for the same error.
/// </summary>
public sealed class SmtpRetryTests
{
    [Theory]
    [InlineData(SmtpStatusCode.ServiceNotAvailable)]      // 421: busy, try later
    [InlineData(SmtpStatusCode.MailboxBusy)]              // 450
    [InlineData(SmtpStatusCode.LocalErrorInProcessing)]   // 451
    [InlineData(SmtpStatusCode.GeneralFailure)]           // no connection made at all
    public void Busy_or_unreachable_is_retried(SmtpStatusCode code) =>
        Assert.True(SmtpEmailSender.IsTransient(new SmtpException(code)));

    [Theory]
    [InlineData(SmtpStatusCode.MailboxUnavailable)]       // 550: quota used up, address refused
    [InlineData((SmtpStatusCode)535)]                     // wrong password
    [InlineData(SmtpStatusCode.TransactionFailed)]        // 554
    public void A_refusal_fails_at_once(SmtpStatusCode code) =>
        Assert.False(SmtpEmailSender.IsTransient(new SmtpException(code)));

    [Fact]
    public void A_dropped_connection_is_retried()
    {
        Assert.True(SmtpEmailSender.IsTransient(new IOException("connection reset")));
        Assert.True(SmtpEmailSender.IsTransient(new SmtpException("failure", new SocketException())));
    }

    [Fact]
    public void Anything_else_is_not() =>
        Assert.False(SmtpEmailSender.IsTransient(new InvalidOperationException()));
}
