using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sacco.Shared.Payments;

namespace Sacco.Modules.Payments.Providers;

/// <summary>Per-provider configuration. Mode "Sandbox" (default) uses the in-process sandbox; "Live" needs real credentials from the secrets manager.</summary>
public sealed class ProviderSettings
{
    public string Mode { get; set; } = "Sandbox";
    public string? BaseUrl { get; set; }
    public string? ConsumerKey { get; set; }
    public string? ConsumerSecret { get; set; }
    public string? ShortCode { get; set; }
    public string? Passkey { get; set; }
    public string? InitiatorName { get; set; }
    public string? SecurityCredential { get; set; }
    public string? CallbackBaseUrl { get; set; }
    public bool IsLive => string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);
}

public sealed class PaymentsSettings
{
    public const string SectionName = "Payments";
    public ProviderSettings MPesa { get; set; } = new();
    public ProviderSettings AirtelMoney { get; set; } = new();
    public ProviderSettings Bank { get; set; } = new();
    /// <summary>Minutes to wait for a callback before verifying with the provider directly.</summary>
    public int CallbackTimeoutMinutes { get; set; } = 3;
    /// <summary>Sandbox only: seconds after initiation to auto-deliver the simulated callback (0 = manual via the simulate endpoint).</summary>
    public int SandboxAutoCallbackSeconds { get; set; } = 0;
    /// <summary>Tenant slug segment used when building callback URLs.</summary>
    public string WebhookPath { get; set; } = "/api/payments/webhooks";
    /// <summary>Source IPs/CIDRs allowed to call the webhook endpoint (e.g. the provider's published callback ranges). Empty = unrestricted (warned at startup in Production).</summary>
    public List<string> WebhookAllowedCidrs { get; set; } = [];
}

/// <summary>
/// Safaricom Daraja implementation: STK push (Lipa na M-Pesa Online), STK query for reconciliation,
/// B2C for payouts. Endpoint paths and payload shapes follow the published Daraja API; verify against
/// the current Daraja documentation before go-live (docs/integrations/payment-providers.md).
/// </summary>
public sealed class DarajaMpesaProvider(IHttpClientFactory httpClientFactory, IOptions<PaymentsSettings> options, ILogger<DarajaMpesaProvider> logger) : IPaymentProvider
{
    private ProviderSettings S => options.Value.MPesa;
    public string ProviderName => ProviderNames.MPesa;
    public bool IsSandbox => false;

    private HttpClient Client()
    {
        var c = httpClientFactory.CreateClient(nameof(DarajaMpesaProvider));
        c.BaseAddress = new Uri(S.BaseUrl ?? throw new InvalidOperationException("Payments:MPesa:BaseUrl is required in Live mode."));
        return c;
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        var c = Client();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{S.ConsumerKey}:{S.ConsumerSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Get, "oauth/v1/generate?grant_type=client_credentials");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        using var res = await c.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        return doc.GetProperty("access_token").GetString()!;
    }

    private (string Timestamp, string Password) StkCredentials()
    {
        var ts = DateTime.UtcNow.AddHours(3).ToString("yyyyMMddHHmmss");
        return (ts, Convert.ToBase64String(Encoding.UTF8.GetBytes($"{S.ShortCode}{S.Passkey}{ts}")));
    }

    public async Task<CollectionResult> InitiateCollectionAsync(CollectionRequest r, CancellationToken ct)
    {
        var token = await AccessTokenAsync(ct);
        var (ts, password) = StkCredentials();
        var body = new
        {
            BusinessShortCode = S.ShortCode, Password = password, Timestamp = ts, TransactionType = "CustomerPayBillOnline",
            Amount = decimal.ToInt32(decimal.Ceiling(r.Amount)), PartyA = r.PhoneNumber, PartyB = S.ShortCode, PhoneNumber = r.PhoneNumber,
            CallBackURL = $"{S.CallbackBaseUrl}/mpesa", AccountReference = r.AccountReference, TransactionDesc = r.Description,
        };
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await c.PostAsJsonAsync("mpesa/stkpush/v1/processrequest", body, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!res.IsSuccessStatusCode || doc.TryGetProperty("errorMessage", out var err))
        {
            var reason = doc.TryGetProperty("errorMessage", out var e) ? e.GetString() : res.ReasonPhrase;
            logger.LogWarning("Daraja STK push rejected for {Reference}: {Reason}", r.OurReference, reason);
            return new CollectionResult(false, null, reason);
        }
        return new CollectionResult(true, doc.GetProperty("CheckoutRequestID").GetString(), null);
    }

    public async Task<DisbursementResult> InitiateDisbursementAsync(DisbursementRequest r, CancellationToken ct)
    {
        var token = await AccessTokenAsync(ct);
        var body = new
        {
            InitiatorName = S.InitiatorName, SecurityCredential = S.SecurityCredential, CommandID = "BusinessPayment",
            Amount = decimal.ToInt32(decimal.Ceiling(r.Amount)), PartyA = S.ShortCode, PartyB = r.Destination, Remarks = r.Description,
            QueueTimeOutURL = $"{S.CallbackBaseUrl}/mpesa", ResultURL = $"{S.CallbackBaseUrl}/mpesa", Occasion = r.OurReference,
        };
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await c.PostAsJsonAsync("mpesa/b2c/v1/paymentrequest", body, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!res.IsSuccessStatusCode || doc.TryGetProperty("errorMessage", out _))
            return new DisbursementResult(false, null, doc.TryGetProperty("errorMessage", out var e) ? e.GetString() : res.ReasonPhrase);
        return new DisbursementResult(true, doc.GetProperty("ConversationID").GetString(), null);
    }

    public async Task<TransactionStatus> VerifyTransactionAsync(string providerRequestId, CancellationToken ct)
    {
        var token = await AccessTokenAsync(ct);
        var (ts, password) = StkCredentials();
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await c.PostAsJsonAsync("mpesa/stkpushquery/v1/query", new { BusinessShortCode = S.ShortCode, Password = password, Timestamp = ts, CheckoutRequestID = providerRequestId }, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!doc.TryGetProperty("ResultCode", out var code)) return new TransactionStatus(ProviderTransactionState.Pending, null, null, null);
        var rc = code.ValueKind == JsonValueKind.String ? code.GetString() : code.GetInt32().ToString();
        return rc == "0"
            ? new TransactionStatus(ProviderTransactionState.Succeeded, null, null, null)
            : new TransactionStatus(ProviderTransactionState.Failed, null, null, doc.TryGetProperty("ResultDesc", out var d) ? d.GetString() : rc);
    }

    /// <summary>Parses STK push (Body.stkCallback) and B2C (Result) callbacks.</summary>
    public Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct)
    {
        try
        {
            var root = JsonDocument.Parse(payload.Body).RootElement;
            if (root.TryGetProperty("Body", out var body) && body.TryGetProperty("stkCallback", out var stk))
            {
                var resultCode = stk.GetProperty("ResultCode").GetInt32();
                var requestId = stk.GetProperty("CheckoutRequestID").GetString();
                string? receipt = null; decimal amount = 0; string? phone = null;
                if (stk.TryGetProperty("CallbackMetadata", out var meta) && meta.TryGetProperty("Item", out var items))
                    foreach (var item in items.EnumerateArray())
                    {
                        var name = item.GetProperty("Name").GetString();
                        if (name == "MpesaReceiptNumber") receipt = item.GetProperty("Value").GetString();
                        else if (name == "Amount") amount = item.GetProperty("Value").GetDecimal();
                        else if (name == "PhoneNumber") phone = item.GetProperty("Value").ToString();
                    }
                var reference = receipt ?? $"STK-{requestId}";
                return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, reference, requestId, null,
                    resultCode == 0 ? ProviderTransactionState.Succeeded : ProviderTransactionState.Failed, amount, phone,
                    resultCode == 0 ? null : stk.GetProperty("ResultDesc").GetString(), DateTimeOffset.UtcNow)));
            }
            if (root.TryGetProperty("Result", out var result))
            {
                var resultCode = result.GetProperty("ResultCode").GetInt32();
                var conversationId = result.GetProperty("ConversationID").GetString();
                var receipt = result.TryGetProperty("TransactionID", out var t) ? t.GetString() : null;
                return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, receipt ?? $"B2C-{conversationId}", conversationId, null,
                    resultCode == 0 ? ProviderTransactionState.Succeeded : ProviderTransactionState.Failed, 0, null,
                    resultCode == 0 ? null : result.GetProperty("ResultDesc").GetString(), DateTimeOffset.UtcNow)));
            }
            return Task.FromResult(WebhookHandlingResult.Invalid("Unrecognised Daraja callback shape"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(WebhookHandlingResult.Invalid($"Malformed Daraja callback: {ex.Message}"));
        }
    }
}
