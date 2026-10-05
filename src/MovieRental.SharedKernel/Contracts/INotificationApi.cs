namespace MovieRental.SharedKernel.Contracts;

public interface IEmailSender
{
    Task SendAsync(EmailRequest request, CancellationToken ct = default);
}

public interface ISmsSender
{
    Task SendAsync(SmsRequest request, CancellationToken ct = default);
}

public sealed record EmailRequest(string To, string Subject, string HtmlBody)
{
    public string? PlainTextBody { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Files sent along with the message — the PDF ticket, for instance.</summary>
    public IReadOnlyList<EmailAttachment>? Attachments { get; init; }
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record SmsRequest(string PhoneNumber, string Text);
