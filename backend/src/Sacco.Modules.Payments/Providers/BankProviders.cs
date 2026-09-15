using System.Collections.Concurrent;
using System.Globalization;
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
/// Destination for a bank payout, parsed from the withdrawal's payout destination:
/// <c>BANKCODE|ACCOUNT|BENEFICIARY NAME|BRANCH</c> (later parts optional) or a bare account/phone number.
/// </summary>
public sealed record BankDestination(string? BankCode, string Account, string? BeneficiaryName, string? BranchCode)
{
    public static BankDestination Parse(string destination)
    {
        var parts = destination.Split('|', StringSplitOptions.TrimEntries);
        return parts.Length == 1 ? new(null, parts[0], null, null) : new(parts[0], parts.Length > 1 ? parts[1] : "", parts.Length > 2 ? parts[2] : null, parts.Length > 3 ? parts[3] : null);
    }
}

/// <summary>
/// The Jenga PIN/password envelope (DFS Partner REST APIs v1.0.8, "PIN &amp; Password Encryption"): AES-256-GCM with a
/// PBKDF2-HMAC-SHA256 key derived from the API key, Base64 of <c>IV(12) || SALT(16) || CIPHERTEXT || TAG(16)</c>.
/// </summary>
public static class JengaCredentialCipher
{
    private const int SaltLength = 16, IvLength = 12, TagLength = 16, KeyLength = 32, Iterations = 65_536;

    public static string Encrypt(string plainText, string apiKey)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var iv = RandomNumberGenerator.GetBytes(IvLength);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(apiKey), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLength];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(iv, plain, cipher, tag);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return Convert.ToBase64String([.. iv, .. salt, .. cipher, .. tag]);
    }
}

/// <summary>
/// Equity Bank / Finserve "Jenga DFS Partner REST APIs" (v1.0.8; UAT https://uat.finserve.africa, production
/// https://api.finserve.africa). Token from <c>authentication/api/v3/authenticate/merchant</c> (Api-Key header, absolute
/// expiry), C2B buy-goods for collections, B2C for payouts, status query by request id. The acknowledgement carries no
/// transaction id, so everything correlates on the <c>shortCode_requestId</c> we generate. Success and failure callbacks
/// have different shapes (flat vs. <c>metadata</c>). Modelled on the mock under <c>mocked-providers/mocked-equity-server</c>.
/// </summary>
public sealed class EquityJengaProvider(IHttpClientFactory httpClientFactory, IOptions<PaymentsSettings> options, ILogger<EquityJengaProvider> logger, TimeProvider? timeProvider = null) : IPaymentProvider
{
    public const string BankCode = "EQUITY";
    public const string HttpClientName = nameof(EquityJengaProvider);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private (string Token, DateTimeOffset ExpiresAt)? _token;

    private ProviderSettings S => options.Value.Banks.TryGetValue(BankCode, out var s) ? s : options.Value.Bank;
    public string ProviderName => ProviderNames.BankPrefix + BankCode;
    public bool IsSandbox => false;

    private HttpClient Client(string? baseUrl = null)
    {
        var c = httpClientFactory.CreateClient(HttpClientName);
        c.BaseAddress = new Uri(baseUrl ?? S.BaseUrl ?? throw new InvalidOperationException("Payments:Banks:EQUITY:BaseUrl is required in Live mode."));
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if (_token is { } cached && cached.ExpiresAt > _time.GetUtcNow().AddSeconds(30)) return cached.Token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is { } again && again.ExpiresAt > _time.GetUtcNow().AddSeconds(30)) return again.Token;
            var c = Client(S.AuthBaseUrl ?? S.BaseUrl);
            using var req = new HttpRequestMessage(HttpMethod.Post, "authentication/api/v3/authenticate/merchant") { Content = JsonContent.Create(new { merchantCode = S.MerchantCode, consumerSecret = S.ConsumerSecret }, options: Json) };
            req.Headers.Add("Api-Key", S.ApiKey ?? throw new InvalidOperationException("Payments:Banks:EQUITY:ApiKey is required."));
            using var res = await c.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Jenga authentication failed: {(int)res.StatusCode} {body}");
            var doc = JsonDocument.Parse(body).RootElement;
            var token = doc.GetProperty("accessToken").GetString()!;
            var expires = doc.TryGetProperty("expiresIn", out var e) && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : _time.GetUtcNow().AddMinutes(10);
            _token = (token, expires);
            return token;
        }
        finally { _tokenLock.Release(); }
    }

    private async Task<HttpClient> AuthorisedClientAsync(CancellationToken ct)
    {
        var c = Client();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        return c;
    }

    /// <summary>Jenga requires <c>shortCode_uniqueRequestId</c>; our reference is already unique per tenant.</summary>
    public string RequestIdFor(string ourReference) => $"{S.ShortCode}_{new string(ourReference.Where(char.IsLetterOrDigit).ToArray())}";
    private string CallbackUrl() => $"{(S.CallbackBaseUrl ?? "https://callbacks.invalid").TrimEnd('/')}/bank:equity{(string.IsNullOrEmpty(S.CallbackToken) ? "" : $"?token={Uri.EscapeDataString(S.CallbackToken)}")}";
    private string EncryptedPin() => JengaCredentialCipher.Encrypt(S.Pin ?? throw new InvalidOperationException("Payments:Banks:EQUITY:Pin is required."), S.ApiKey ?? throw new InvalidOperationException("Payments:Banks:EQUITY:ApiKey is required."));

    public Task<CollectionResult> InitiateCollectionAsync(CollectionRequest r, CancellationToken ct) => SubmitAsync("momo-apis/api/v1/transaction/c2b/customer-initiated-payment", r.OurReference, r.Amount, r.PhoneNumber, r.Description, ct)
        .ContinueWith(t => new CollectionResult(t.Result.Accepted, t.Result.RequestId, t.Result.Reason), ct);

    public Task<DisbursementResult> InitiateDisbursementAsync(DisbursementRequest r, CancellationToken ct) => SubmitAsync("momo-apis/api/v1/transaction/b2c/business-to-customer-payment", r.OurReference, r.Amount, BankDestination.Parse(r.Destination).Account, r.Description, ct)
        .ContinueWith(t => new DisbursementResult(t.Result.Accepted, t.Result.RequestId, t.Result.Reason), ct);

    private async Task<(bool Accepted, string? RequestId, string? Reason)> SubmitAsync(string path, string ourReference, decimal amount, string msisdn, string remarks, CancellationToken ct)
    {
        var requestId = RequestIdFor(ourReference);
        var body = new { requestId, amount = amount.ToString("0.##", CultureInfo.InvariantCulture), currency = S.Currency ?? "KES", shortCode = S.ShortCode, pin = EncryptedPin(), callbackUrl = CallbackUrl(), msisdn = new string(msisdn.Where(char.IsDigit).ToArray()), remarks = remarks.Length > 100 ? remarks[..100] : remarks };
        try
        {
            var c = await AuthorisedClientAsync(ct);
            using var res = await c.PostAsJsonAsync(path, body, Json, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            var doc = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement;
            if (res.IsSuccessStatusCode && doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("responseCode", out var code) && code.GetString() == "0")
                return (true, requestId, null);
            var reason = doc.ValueKind == JsonValueKind.Object ? (doc.TryGetProperty("responseDesc", out var d) ? d.GetString() : doc.TryGetProperty("message", out var m) ? m.GetString() : null) : null;
            logger.LogWarning("Jenga rejected {RequestId}: {Status} {Reason}", requestId, (int)res.StatusCode, reason ?? text);
            return (false, null, reason ?? $"Jenga responded {(int)res.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Jenga request failed for {RequestId}", requestId);
            return (false, null, ex.Message);
        }
    }

    public async Task<TransactionStatus> VerifyTransactionAsync(string providerRequestId, CancellationToken ct)
    {
        var c = await AuthorisedClientAsync(ct);
        using var res = await c.PostAsJsonAsync("momo-apis/api/v1/transaction/query-transaction-status", new { transactionId = providerRequestId }, Json, ct);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement;
        var code = doc.TryGetProperty("code", out var cd) ? cd.GetString() : null;
        var meta = doc.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        var status = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("status", out var st) ? st.GetString() : null;
        var receipt = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("transactionId", out var tid) && tid.ValueKind == JsonValueKind.String ? tid.GetString() : null;
        if (code == "3011" && receipt is null) return new TransactionStatus(ProviderTransactionState.NotFound, null, null, doc.TryGetProperty("message", out var msg) ? msg.GetString() : "Transaction not found");
        return status?.ToUpperInvariant() switch
        {
            "SUCCESS" => new TransactionStatus(ProviderTransactionState.Succeeded, receipt, null, null),
            "FAILED" => new TransactionStatus(ProviderTransactionState.Failed, receipt, null, doc.TryGetProperty("message", out var fm) ? fm.GetString() : "Transaction failed"),
            _ => new TransactionStatus(ProviderTransactionState.Pending, receipt, null, null),
        };
    }

    /// <summary>Success: flat with <c>transactionReference</c>; failure: <c>metadata.requestReference</c>. Both carry the transaction id (the receipt).</summary>
    public Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(S.CallbackToken))
        {
            var presented = payload.Headers.TryGetValue("query:token", out var q) ? q : null;
            if (presented is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(S.CallbackToken)))
                return Task.FromResult(WebhookHandlingResult.Invalid("Jenga callback token mismatch"));
        }
        try
        {
            var root = JsonDocument.Parse(payload.Body).RootElement;
            if (root.TryGetProperty("transactionReference", out var reference))
            {
                var resultType = root.TryGetProperty("resultType", out var rt) ? rt.GetString() : null;
                var receipt = root.TryGetProperty("transactionId", out var tid) ? tid.GetString() : null;
                var ok = string.Equals(resultType, "SUCCESS", StringComparison.OrdinalIgnoreCase);
                return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, receipt ?? $"JENGA-{reference.GetString()}", reference.GetString(), null,
                    ok ? ProviderTransactionState.Succeeded : ProviderTransactionState.Failed, 0, null, ok ? null : root.TryGetProperty("resultDesc", out var rd) ? rd.GetString() : resultType, _time.GetUtcNow())));
            }
            if (root.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("requestReference", out var rr))
            {
                var status = meta.TryGetProperty("status", out var st) ? st.GetString() : "FAILED";
                var receipt = meta.TryGetProperty("transactionId", out var tid) && tid.ValueKind == JsonValueKind.String ? tid.GetString() : null;
                var reason = meta.TryGetProperty("errorMessage", out var em) ? em.GetString() : root.TryGetProperty("message", out var msg) ? msg.GetString() : status;
                var ok = string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase);
                return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, receipt ?? $"JENGA-{rr.GetString()}", rr.GetString(), null, ok ? ProviderTransactionState.Succeeded : ProviderTransactionState.Failed, 0, null, ok ? null : reason, _time.GetUtcNow())));
            }
            return Task.FromResult(WebhookHandlingResult.Invalid("Unrecognised Jenga callback shape"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(WebhookHandlingResult.Invalid($"Malformed Jenga callback: {ex.Message}"));
        }
    }
}

/// <summary>
/// NCBA Payments API: one synchronous transfer endpoint (<c>api/v1/payments/transfer</c>) authenticated with the
/// <c>API-Key</c>/<c>API-User</c> headers; Pesalink to any local bank, Internal to NCBA accounts, EFT/RTGS, Mwallet. The
/// bank answers with the receipt (<c>TxnReferenceNo</c>) or an error code — there are no callbacks — so a disbursement is
/// settled synchronously and the saga finalises at once. References must be at most 12 characters. Payout only: bank
/// collections arrive as unsolicited credits. Modelled on <c>mocked-providers/mocked-ncba</c> and the NCBA testing guide.
/// </summary>
public sealed class NcbaProvider(IHttpClientFactory httpClientFactory, IOptions<PaymentsSettings> options, ILogger<NcbaProvider> logger, TimeProvider? timeProvider = null) : IPaymentProvider
{
    public const string BankCode = "NCBA";
    public const string HttpClientName = nameof(NcbaProvider);
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (string Receipt, decimal Amount)> _settled = new();

    private ProviderSettings S => options.Value.Banks.TryGetValue(BankCode, out var s) ? s : options.Value.Bank;
    public string ProviderName => ProviderNames.BankPrefix + BankCode;
    public bool IsSandbox => false;

    /// <summary>NCBA caps references at 12 characters; derive a stable, unique short form of our reference.</summary>
    public static string ShortReference(string ourReference)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ourReference));
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new StringBuilder(12);
        for (var i = 0; i < 12; i++) sb.Append(alphabet[hash[i] % alphabet.Length]);
        return sb.ToString();
    }

    public Task<CollectionResult> InitiateCollectionAsync(CollectionRequest r, CancellationToken ct)
        => Task.FromResult(new CollectionResult(false, null, "NCBA does not offer customer-initiated collections; bank credits arrive as unsolicited confirmations."));

    public async Task<DisbursementResult> InitiateDisbursementAsync(DisbursementRequest r, CancellationToken ct)
    {
        var dest = BankDestination.Parse(r.Destination);
        var reference = ShortReference(r.OurReference);
        var isMobile = dest.BankCode is null && dest.Account.StartsWith("254") && dest.Account.Length == 12;
        var body = new Dictionary<string, object?>
        {
            ["BankCode"] = dest.BankCode ?? S.DefaultBankCode ?? "",
            ["BankSwiftCode"] = "",
            ["BranchCode"] = dest.BranchCode ?? S.DefaultBranchCode ?? "",
            ["BeneficiaryAccountName"] = dest.BeneficiaryName ?? "Member",
            ["BeneficiaryName"] = dest.BeneficiaryName ?? "Member",
            ["Country"] = S.Country ?? "Kenya",
            ["TranType"] = isMobile ? "Mwallet" : "Pesalink",
            ["Reference"] = reference,
            ["Currency"] = S.Currency ?? "KES",
            ["Account"] = dest.Account,
            ["Amount"] = decimal.Round(r.Amount, 2),
            ["Narration"] = new string(r.Description.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim(),
            ["Transaction Date"] = _time.GetUtcNow().AddHours(3).ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
        };
        try
        {
            var c = httpClientFactory.CreateClient(HttpClientName);
            c.BaseAddress = new Uri(S.BaseUrl ?? throw new InvalidOperationException("Payments:Banks:NCBA:BaseUrl is required in Live mode."));
            c.DefaultRequestHeaders.Add("API-Key", S.ApiKey ?? throw new InvalidOperationException("Payments:Banks:NCBA:ApiKey is required."));
            c.DefaultRequestHeaders.Add("API-User", S.ApiUser ?? throw new InvalidOperationException("Payments:Banks:NCBA:ApiUser is required."));
            using var res = await c.PostAsJsonAsync("api/v1/payments/transfer", body, Json, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            var doc = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement;
            var receipt = doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("TxnReferenceNo", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (res.IsSuccessStatusCode && !string.IsNullOrEmpty(receipt))
            {
                _settled[reference] = (receipt!, r.Amount);
                return new DisbursementResult(true, reference, null, Completed: true, ProviderTransactionReference: receipt);
            }
            var code = doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("ErrorCode", out var ec) ? ec.GetString() : null;
            var desc = doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("ErrorDescription", out var ed) ? ed.GetString() : null;
            var reason = desc is null ? $"NCBA responded {(int)res.StatusCode}: {text}" : $"NCBA error {code}: {desc}";
            logger.LogWarning("NCBA rejected {Reference}: {Reason}", reference, reason);
            return new DisbursementResult(false, null, reason);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(ex, "NCBA transfer failed for {Reference}", reference);
            return new DisbursementResult(false, null, ex.Message);
        }
    }

    public Task<TransactionStatus> VerifyTransactionAsync(string providerRequestId, CancellationToken ct)
        => Task.FromResult(_settled.TryGetValue(providerRequestId, out var s)
            ? new TransactionStatus(ProviderTransactionState.Succeeded, s.Receipt, s.Amount, null)
            : new TransactionStatus(ProviderTransactionState.NotFound, null, null, "NCBA settles synchronously; no record of this reference in this process"));

    public Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct)
        => Task.FromResult(WebhookHandlingResult.Invalid("NCBA sends no callbacks; transfers settle synchronously"));
}
