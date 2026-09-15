using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sacco.Shared.Payments;

namespace Sacco.Modules.Payments.Providers;

/// <summary>
/// Airtel Money Open API (https://openapi.airtel.africa, UAT at openapiuat.airtel.africa): OAuth2 client-credentials token,
/// USSD push (<c>merchant/v1/payments/</c>) for collections, transaction enquiry (<c>standard/v1/payments/{id}</c>),
/// B2C disbursement (<c>standard/v2/disbursements/</c>) with the PIN RSA-encrypted under Airtel's public key, and
/// disbursement enquiry. Written from the shape of a working integration (`reference/Brij.AirtelMoney`); verify against
/// the current Airtel developer portal and the UAT sandbox before go-live (docs/integrations/payment-providers.md).
///
/// Callbacks arrive as <c>{"transaction":{"id":…,"message":…,"status_code":"TS|TF|TIP","airtel_money_id":…},"hash":…}</c>.
/// Airtel cannot send custom headers, so the callback URL registered with Airtel carries <c>?token=…</c>, checked against
/// <c>Payments:AirtelMoney:CallbackToken</c>; the webhook endpoint also applies <c>Payments:WebhookAllowedCidrs</c>.
/// </summary>
public sealed class AirtelMoneyProvider(IHttpClientFactory httpClientFactory, IOptions<PaymentsSettings> options, ILogger<AirtelMoneyProvider> logger, TimeProvider? timeProvider = null) : IPaymentProvider
{
    public const string HttpClientName = nameof(AirtelMoneyProvider);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private (string Token, DateTimeOffset ExpiresAt)? _token;

    private ProviderSettings S => options.Value.AirtelMoney;
    public string ProviderName => ProviderNames.AirtelMoney;
    public bool IsSandbox => false;

    private HttpClient Client()
    {
        var c = httpClientFactory.CreateClient(HttpClientName);
        c.BaseAddress = new Uri(S.BaseUrl ?? throw new InvalidOperationException("Payments:AirtelMoney:BaseUrl is required in Live mode."));
        return c;
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && cached.ExpiresAt > _time.GetUtcNow()) return cached.Token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is { } again && again.ExpiresAt > _time.GetUtcNow()) return again.Token;
            var c = Client();
            // The documented API reads the credentials from the JSON body; some gateways (and the local mock) want HTTP Basic. Send both.
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{S.ClientId ?? S.ConsumerKey}:{S.ClientSecret ?? S.ConsumerSecret}")));
            using var res = await c.PostAsJsonAsync("auth/oauth2/token", new { client_id = S.ClientId ?? S.ConsumerKey, client_secret = S.ClientSecret ?? S.ConsumerSecret, grant_type = "client_credentials" }, Json, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Airtel Money token request failed: {(int)res.StatusCode} {body}");
            var doc = JsonDocument.Parse(body).RootElement;
            var token = doc.GetProperty("access_token").GetString()!;
            var expires = doc.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 180;
            _token = (token, _time.GetUtcNow().AddSeconds(Math.Max(30, expires - 60)));
            return token;
        }
        finally { _tokenLock.Release(); }
    }

    private async Task<HttpClient> AuthorisedClientAsync(CancellationToken ct)
    {
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        c.DefaultRequestHeaders.Add("X-Country", S.Country ?? "KE");
        c.DefaultRequestHeaders.Add("X-Currency", S.Currency ?? "KES");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        if (!string.IsNullOrEmpty(S.CallbackBaseUrl)) c.DefaultRequestHeaders.Add("X-Callback-Url", CallbackUrl());
        return c;
    }

    private string CallbackUrl() => $"{S.CallbackBaseUrl!.TrimEnd('/')}/airtelmoney{(string.IsNullOrEmpty(S.CallbackToken) ? "" : $"?token={Uri.EscapeDataString(S.CallbackToken)}")}";
    private object Reference(string text) => S.AirtelReferenceAsObject ? new { id = text } : text;

    /// <summary>Airtel wants the national number: 2547XXXXXXXX → 7XXXXXXXX, 07XXXXXXXX → 7XXXXXXXX.</summary>
    public static string NationalMsisdn(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("254") && digits.Length == 12) return digits[3..];
        if (digits.StartsWith('0') && digits.Length == 10) return digits[1..];
        return digits;
    }

    public async Task<CollectionResult> InitiateCollectionAsync(CollectionRequest r, CancellationToken ct)
    {
        var country = S.Country ?? "KE"; var currency = S.Currency ?? "KES";
        var body = new
        {
            reference = Reference(Truncate(r.Description, 64)),
            subscriber = new { country, currency, msisdn = NationalMsisdn(r.PhoneNumber) },
            transaction = new { amount = decimal.ToInt32(decimal.Ceiling(r.Amount)), country, currency, id = r.OurReference },
        };
        try
        {
            var c = await AuthorisedClientAsync(ct);
            using var res = await c.PostAsJsonAsync("merchant/v1/payments/", body, Json, ct);
            var doc = await ReadJsonAsync(res, ct);
            if (!IsSuccess(res, doc, out var message))
            {
                logger.LogWarning("Airtel Money USSD push rejected for {Reference}: {Reason}", r.OurReference, message);
                return new CollectionResult(false, null, message);
            }
            // Airtel echoes our transaction id; enquiries and callbacks key on it.
            var id = doc.GetProperty("data").GetProperty("transaction").TryGetProperty("id", out var idEl) ? idEl.GetString() : r.OurReference;
            return new CollectionResult(true, id ?? r.OurReference, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Airtel Money USSD push failed for {Reference}", r.OurReference);
            return new CollectionResult(false, null, ex.Message);
        }
    }

    public async Task<DisbursementResult> InitiateDisbursementAsync(DisbursementRequest r, CancellationToken ct)
    {
        var currency = S.Currency ?? "KES";
        var body = new
        {
            payee = new { currency, msisdn = NationalMsisdn(r.Destination), name = Truncate(r.Description, 40) },
            reference = Reference(Truncate(r.Description, 64)),
            pin = EncryptPin(S.DisbursementPin ?? throw new InvalidOperationException("Payments:AirtelMoney:DisbursementPin is required for payouts."), S.DisbursementPublicKey ?? throw new InvalidOperationException("Payments:AirtelMoney:DisbursementPublicKey is required for payouts.")),
            transaction = new { amount = decimal.ToInt32(decimal.Ceiling(r.Amount)), id = r.OurReference, type = "B2C", country = S.Country ?? "KE", currency },
        };
        try
        {
            var c = await AuthorisedClientAsync(ct);
            using var res = await c.PostAsJsonAsync(S.AirtelDisbursementPath, body, Json, ct);
            var doc = await ReadJsonAsync(res, ct);
            if (!IsSuccess(res, doc, out var message))
            {
                logger.LogWarning("Airtel Money disbursement rejected for {Reference}: {Reason}", r.OurReference, message);
                return new DisbursementResult(false, null, message);
            }
            var tx = doc.GetProperty("data").GetProperty("transaction");
            var id = tx.TryGetProperty("id", out var idEl) ? idEl.GetString() : r.OurReference;
            return new DisbursementResult(true, id ?? r.OurReference, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Airtel Money disbursement failed for {Reference}", r.OurReference);
            return new DisbursementResult(false, null, ex.Message);
        }
    }

    /// <summary>Collections and disbursements share our transaction id; try the payments enquiry first, then the disbursement enquiry.</summary>
    public async Task<TransactionStatus> VerifyTransactionAsync(string providerRequestId, CancellationToken ct)
    {
        var c = await AuthorisedClientAsync(ct);
        foreach (var path in new[] { $"standard/v1/payments/{Uri.EscapeDataString(providerRequestId)}", $"standard/v2/disbursements/{Uri.EscapeDataString(providerRequestId)}" })
        {
            using var res = await c.GetAsync(path, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
            var doc = await ReadJsonAsync(res, ct);
            if (!IsSuccess(res, doc, out var message))
            {
                if (message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) continue;
                return new TransactionStatus(ProviderTransactionState.Pending, null, null, message);
            }
            var tx = doc.GetProperty("data").GetProperty("transaction");
            var status = tx.TryGetProperty("status", out var st) ? st.GetString() : null;
            var receipt = tx.TryGetProperty("airtel_money_id", out var am) ? am.GetString() : null;
            var reason = tx.TryGetProperty("message", out var msg) ? msg.GetString() : null;
            return MapStatus(status) switch
            {
                ProviderTransactionState.Succeeded => new TransactionStatus(ProviderTransactionState.Succeeded, receipt ?? $"AM-{providerRequestId}", null, null),
                ProviderTransactionState.Failed => new TransactionStatus(ProviderTransactionState.Failed, receipt, null, reason ?? status),
                _ => new TransactionStatus(ProviderTransactionState.Pending, receipt, null, reason),
            };
        }
        return new TransactionStatus(ProviderTransactionState.NotFound, null, null, "Airtel Money has no record of this transaction");
    }

    public Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(S.CallbackToken))
        {
            var presented = payload.Headers.TryGetValue("query:token", out var q) ? q : payload.Headers.TryGetValue("X-Callback-Token", out var h) ? h : null;
            if (presented is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(S.CallbackToken)))
                return Task.FromResult(WebhookHandlingResult.Invalid("Airtel Money callback token mismatch"));
        }
        try
        {
            var root = JsonDocument.Parse(payload.Body).RootElement;
            if (!root.TryGetProperty("transaction", out var tx)) return Task.FromResult(WebhookHandlingResult.Invalid("Unrecognised Airtel Money callback shape"));
            var id = tx.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id)) return Task.FromResult(WebhookHandlingResult.Invalid("transaction.id is required"));
            var statusCode = tx.TryGetProperty("status_code", out var sc) ? sc.GetString() : tx.TryGetProperty("status", out var s2) ? s2.GetString() : null;
            var receipt = tx.TryGetProperty("airtel_money_id", out var am) ? am.GetString() : null;
            var message = tx.TryGetProperty("message", out var m) ? m.GetString() : null;
            var state = MapStatus(statusCode);
            if (state == ProviderTransactionState.Pending)
                return Task.FromResult(WebhookHandlingResult.Invalid($"Airtel Money reports {statusCode ?? "unknown"} (still in progress); waiting for the final callback")); // a 400 makes Airtel retry with the final status
            return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, string.IsNullOrWhiteSpace(receipt) ? $"AM-{id}" : receipt!, id, null, state, 0, null,
                state == ProviderTransactionState.Failed ? message ?? statusCode : null, _time.GetUtcNow())));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(WebhookHandlingResult.Invalid($"Malformed Airtel Money callback: {ex.Message}"));
        }
    }

    /// <summary>TS = success, TF = failed, TA = ambiguous, TIP = in progress.</summary>
    public static ProviderTransactionState MapStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "TS" or "SUCCESS" => ProviderTransactionState.Succeeded,
        "TF" or "FAILED" => ProviderTransactionState.Failed,
        _ => ProviderTransactionState.Pending,
    };

    /// <summary>RSA/PKCS#1 v1.5 of the PIN under Airtel's public key (XML as issued by Airtel, or a PEM/base64 SubjectPublicKeyInfo), base64-encoded.</summary>
    public static string EncryptPin(string pin, string publicKey)
    {
        using var rsa = RSA.Create();
        var key = publicKey.Trim();
        if (key.StartsWith("<")) rsa.FromXmlString(key);
        else if (key.StartsWith("-----")) rsa.ImportFromPem(key);
        else rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
        return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(pin), RSAEncryptionPadding.Pkcs1));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var text = await res.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}").RootElement : JsonDocument.Parse(text).RootElement;
    }

    private static bool IsSuccess(HttpResponseMessage res, JsonElement doc, out string? message)
    {
        message = doc.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Object && st.TryGetProperty("message", out var m) ? m.GetString() : res.ReasonPhrase;
        if (!res.IsSuccessStatusCode) return false;
        if (doc.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object && status.TryGetProperty("success", out var ok)) return ok.GetBoolean();
        return doc.TryGetProperty("data", out _);
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "SACCO" : s.Length <= max ? s : s[..max];
}
