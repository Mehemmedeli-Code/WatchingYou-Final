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
}

public sealed record SmsRequest(string PhoneNumber, string Text);
