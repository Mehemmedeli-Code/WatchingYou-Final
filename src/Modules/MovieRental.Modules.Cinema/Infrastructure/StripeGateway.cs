using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MovieRental.Modules.Cinema.Infrastructure;

public sealed class StripeOptions
{
    public const string SectionName = "Payments:Stripe";

    /// <summary>sk_test_… from the Stripe dashboard. Empty means Stripe is off and only the
    /// built-in test checkout is offered. Keep it in user secrets, never in appsettings.json.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Stripe supports AZN; any ISO code Stripe accepts works.</summary>
    public string Currency { get; set; } = "azn";
}

public sealed record StripeCheckoutRequest(
    Guid PaymentId, string Reference, string Description, decimal Amount,
    string SuccessUrl, string CancelUrl, string? CustomerEmail, DateTime ExpiresAtUtc);

public sealed record StripeSession(string Id, string Url);

/// <param name="Paid">Stripe's payment_status is "paid".</param>
public sealed record StripeSessionStatus(
    string Id, bool Paid, long AmountTotal, string Currency, string? ClientReferenceId, string? PaymentIntentId);

public interface IStripeGateway
{
    bool Enabled { get; }
    Task<StripeSession> CreateCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct = default);
    Task<StripeSessionStatus> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task RefundAsync(string paymentIntentId, decimal amount, CancellationToken ct = default);
    long ToMinorUnits(decimal amount);
}

/// <summary>
/// Stripe Checkout over Stripe's REST API, with no SDK.
///
/// Stripe hosts the card form, so no card number ever reaches this server — which is the whole
/// point of using it. The flow is: create a Checkout Session, send the browser to its URL,
/// and when Stripe sends the browser back, ask Stripe (server to server) whether that session
/// was paid. The browser's word that it paid is never taken on trust.
///
/// Only test keys (sk_test_…) should be used for the project; test card 4242 4242 4242 4242.
/// </summary>
internal sealed class StripeGateway(
    IHttpClientFactory factory, IOptions<StripeOptions> options, ILogger<StripeGateway> logger) : IStripeGateway
{
    public const string ClientName = "stripe";
    private readonly StripeOptions _options = options.Value;

    public bool Enabled => !string.IsNullOrWhiteSpace(_options.SecretKey);

    public long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    public async Task<StripeSession> CreateCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = request.SuccessUrl,
            ["cancel_url"] = request.CancelUrl,
            ["client_reference_id"] = request.PaymentId.ToString(),
            ["metadata[payment_id]"] = request.PaymentId.ToString(),
            ["metadata[reference]"] = request.Reference,
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = _options.Currency.ToLowerInvariant(),
            ["line_items[0][price_data][unit_amount]"] = ToMinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture),
            ["line_items[0][price_data][product_data][name]"] = request.Description,
            // Stripe's own minimum is thirty minutes from now.
            ["expires_at"] = new DateTimeOffset(DateTime.SpecifyKind(request.ExpiresAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(request.CustomerEmail)) form["customer_email"] = request.CustomerEmail;

        using var json = await SendAsync(HttpMethod.Post, "v1/checkout/sessions", form, ct);
        var root = json.RootElement;
        return new StripeSession(root.GetProperty("id").GetString()!, root.GetProperty("url").GetString()!);
    }

    public async Task<StripeSessionStatus> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using var json = await SendAsync(HttpMethod.Get, $"v1/checkout/sessions/{Uri.EscapeDataString(sessionId)}", null, ct);
        var root = json.RootElement;

        return new StripeSessionStatus(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("payment_status", out var status) && status.GetString() == "paid",
            root.TryGetProperty("amount_total", out var total) && total.ValueKind == JsonValueKind.Number ? total.GetInt64() : 0,
            root.TryGetProperty("currency", out var currency) ? currency.GetString() ?? "" : "",
            root.TryGetProperty("client_reference_id", out var reference) ? reference.GetString() : null,
            root.TryGetProperty("payment_intent", out var intent) && intent.ValueKind == JsonValueKind.String ? intent.GetString() : null);
    }

    public async Task RefundAsync(string paymentIntentId, decimal amount, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["payment_intent"] = paymentIntentId,
            ["amount"] = ToMinorUnits(amount).ToString(CultureInfo.InvariantCulture)
        };
        // Same key for the same payment: a retry after a timeout or a failed save here cannot
        // refund twice. Stripe remembers keys for 24 hours.
        using var _ = await SendAsync(HttpMethod.Post, "v1/refunds", form, ct, idempotencyKey: $"refund-{paymentIntentId}");
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, Dictionary<string, string>? form, CancellationToken ct, string? idempotencyKey = null)
    {
        if (!Enabled) throw new InvalidOperationException("Stripe is not configured (Payments:Stripe:SecretKey).");

        var client = factory.CreateClient(ClientName);
        using var message = new HttpRequestMessage(method, new Uri(new Uri("https://api.stripe.com/"), path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);
        if (form is not null) message.Content = new FormUrlEncodedContent(form);
        if (idempotencyKey is not null) message.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await client.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Stripe's error message is safe to log; it never echoes the key.
            logger.LogError("Stripe {Method} {Path} failed with {Status}: {Body}", method, path, (int)response.StatusCode, body);
            throw new StripeException(ReadError(body) ?? $"Stripe answered {(int)response.StatusCode}.");
        }

        return JsonDocument.Parse(body);
    }

    private static string? ReadError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class StripeException(string message) : Exception(message);
