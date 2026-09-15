using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    // Airtel Money
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }
    /// <summary>Disbursement PIN; sent RSA-encrypted under <see cref="DisbursementPublicKey"/>. Secrets manager only.</summary>
    public string? DisbursementPin { get; set; }
    /// <summary>Airtel's public key (XML, PEM or base64 SPKI) used to encrypt the PIN.</summary>
    public string? DisbursementPublicKey { get; set; }
    /// <summary>Secret appended as <c>?token=</c> to the callback URL registered with the provider.</summary>
    public string? CallbackToken { get; set; }
    // Provider-specific paths that differ between API versions and the local mock servers.
    public string StkQueryPath { get; set; } = "mpesa/stkpushquery/v1/query";
    public string AirtelDisbursementPath { get; set; } = "standard/v2/disbursements/";
    /// <summary>Airtel: send <c>reference</c> as <c>{"id": …}</c> (mock server) instead of a plain string (documented API).</summary>
    public bool AirtelReferenceAsObject { get; set; }
    // Equity (Jenga)
    public string? AuthBaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? MerchantCode { get; set; }
    public string? Pin { get; set; }
    // NCBA
    public string? ApiUser { get; set; }
    public string? DefaultBankCode { get; set; }
    public string? DefaultBranchCode { get; set; }
    public bool IsLive => string.Equals(Mode, "Live", StringComparison.OrdinalIgnoreCase);
}

public sealed class PaymentsSettings
{
    public const string SectionName = "Payments";
    public ProviderSettings MPesa { get; set; } = new();
    public ProviderSettings AirtelMoney { get; set; } = new();
    public ProviderSettings Bank { get; set; } = new();
    /// <summary>Live bank integrations keyed by bank code (EQUITY, NCBA); a code without a Live entry is unavailable.</summary>
    public Dictionary<string, ProviderSettings> Banks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Provider used for withdrawals paid by bank transfer, e.g. "Bank:NCBA".</summary>
    public string DefaultBankProvider { get; set; } = ProviderNames.SandboxBank;
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
    /// <summary>Daraja request fields are Pascal-case (<c>BusinessShortCode</c>, <c>CallBackURL</c>); the default web policy would camel-case them.</summary>
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string ProviderName => ProviderNames.MPesa;
    public bool IsSandbox => false;

    private HttpClient Client()
    {
        var c = httpClientFactory.CreateClient(nameof(DarajaMpesaProvider));
        c.BaseAddress = new Uri(S.BaseUrl ?? throw new InvalidOperationException("Payments:MPesa:BaseUrl is required in Live mode."));
        return c;
    }

    /// <summary>
    /// Daraja's documented casing is inconsistent (<c>CheckoutRequestID</c>, <c>errorMessage</c>, <c>ResultCode</c>) and
    /// gateways that front it (including the mock servers) may camel-case responses, so every lookup is case-insensitive.
    /// </summary>
    internal static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (e.TryGetProperty(name, out var exact)) return exact;
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string? Str(JsonElement e, string name) => Prop(e, name) is { } v ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : v.ToString()) : null;

    private static string? Rejection(HttpResponseMessage res, JsonElement doc)
    {
        if (Str(doc, "errorMessage") is { } err) return err;
        if (!res.IsSuccessStatusCode) return res.ReasonPhrase ?? ((int)res.StatusCode).ToString();
        var code = Str(doc, "ResponseCode");
        return code is not null && code != "0" ? Str(doc, "ResponseDescription") ?? $"ResponseCode {code}" : null;
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
        return Str(doc, "access_token") ?? throw new InvalidOperationException("Daraja token response had no access_token.");
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
        using var res = await c.PostAsJsonAsync("mpesa/stkpush/v1/processrequest", body, Json, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (Rejection(res, doc) is { } reason)
        {
            logger.LogWarning("Daraja STK push rejected for {Reference}: {Reason}", r.OurReference, reason);
            return new CollectionResult(false, null, reason);
        }
        var checkoutId = Str(doc, "CheckoutRequestID");
        return checkoutId is null ? new CollectionResult(false, null, "Daraja STK response had no CheckoutRequestID") : new CollectionResult(true, checkoutId, null);
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
        using var res = await c.PostAsJsonAsync("mpesa/b2c/v1/paymentrequest", body, Json, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (Rejection(res, doc) is { } reason)
        {
            logger.LogWarning("Daraja B2C rejected for {Reference}: {Reason}", r.OurReference, reason);
            return new DisbursementResult(false, null, reason);
        }
        var conversationId = Str(doc, "ConversationID");
        return conversationId is null ? new DisbursementResult(false, null, "Daraja B2C response had no ConversationID") : new DisbursementResult(true, conversationId, null);
    }

    public async Task<TransactionStatus> VerifyTransactionAsync(string providerRequestId, CancellationToken ct)
    {
        var token = await AccessTokenAsync(ct);
        var (ts, password) = StkCredentials();
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await c.PostAsJsonAsync(S.StkQueryPath, new { BusinessShortCode = S.ShortCode, Password = password, Timestamp = ts, CheckoutRequestID = providerRequestId }, Json, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        var rc = Str(doc, "ResultCode");
        if (rc is null) return new TransactionStatus(ProviderTransactionState.Pending, null, null, null);
        return rc == "0"
            ? new TransactionStatus(ProviderTransactionState.Succeeded, Str(doc, "MpesaReceiptNumber"), null, null)
            : new TransactionStatus(ProviderTransactionState.Failed, null, null, Str(doc, "ResultDesc") ?? rc);
    }

    /// <summary>Parses STK push (Body.stkCallback) and B2C (Result) callbacks.</summary>
    public Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct)
    {
        try
        {
            var root = JsonDocument.Parse(payload.Body).RootElement;
            if (Prop(root, "Body") is { } body && Prop(body, "stkCallback") is { } stk)
            {
                var resultCode = Str(stk, "ResultCode") ?? throw new KeyNotFoundException("ResultCode");
                var requestId = Str(stk, "CheckoutRequestID") ?? throw new KeyNotFoundException("CheckoutRequestID");
                string? receipt = null; decimal amount = 0; string? phone = null;
                if (Prop(stk, "CallbackMetadata") is { } meta && Prop(meta, "Item") is { ValueKind: JsonValueKind.Array } items)
                    foreach (var item in items.EnumerateArray())
                    {
                        var name = Str(item, "Name");
                        var value = Prop(item, "Value");
                        if (value is null || value.Value.ValueKind is JsonValueKind.Null) continue;
                        if (name == "MpesaReceiptNumber") receipt = value.Value.ToString();
                        else if (name == "Amount") amount = value.Value.ValueKind == JsonValueKind.Number ? value.Value.GetDecimal() : decimal.Parse(value.Value.ToString(), CultureInfo.InvariantCulture);
                        else if (name == "PhoneNumber") phone = value.Value.ToString();
                    }
                var ok = resultCode == "0";
                return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, receipt ?? $"STK-{requestId}", requestId, null,
                    ok ? ProviderTransactionState.Succeeded : ProviderTransactionState.Failed, amount, phone,
                    ok ? null : Str(stk, "ResultDesc") ?? resultCode, DateTimeOffset.UtcNow)));
            }
            if (Prop(root, "Result") is { } result)
            {
                var resultCode = Str(result, "ResultCode") ?? throw new KeyNotFoundException("ResultCode");
                var conversationId = Str(result, "ConversationID") ?? throw new KeyNotFoundException("ConversationID");
                var receipt = Str(result, "TransactionID");
                var ok = resultCode == "0";
                return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, receipt ?? $"B2C-{conversationId}", conversationId, null,
                    ok ? ProviderTransactionState.Succeeded : ProviderTransactionState.Failed, 0, null,
                    ok ? null : Str(result, "ResultDesc") ?? resultCode, DateTimeOffset.UtcNow)));
            }
            return Task.FromResult(WebhookHandlingResult.Invalid("Unrecognised Daraja callback shape"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(WebhookHandlingResult.Invalid($"Malformed Daraja callback: {ex.Message}"));
        }
    }
}
