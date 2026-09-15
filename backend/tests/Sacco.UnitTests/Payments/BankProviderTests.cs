using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sacco.Modules.Payments.Providers;
using Sacco.Shared.Payments;
using Shouldly;

namespace Sacco.UnitTests.Payments;

/// <summary>Equity (Jenga) and NCBA providers driven through a fake HTTP handler: request shapes, the AES-GCM PIN envelope, status mapping, callbacks, synchronous settlement.</summary>
public sealed class BankProviderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];
        public Func<HttpRequestMessage, string, (HttpStatusCode, string)> Respond { get; set; } = (_, _) => (HttpStatusCode.OK, "{}");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            var (status, json) = Respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Factory(FakeHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }

    private static ProviderSettings Equity() => new() { Mode = "Live", BaseUrl = "https://uat.finserve.africa/", ApiKey = "local-api-key", MerchantCode = "0582910862", ConsumerSecret = "local-consumer-secret", ShortCode = "800800", Pin = "2580", CallbackBaseUrl = "https://api.example/api/payments/webhooks/demo", CallbackToken = "eq-token" };
    private static ProviderSettings Ncba() => new() { Mode = "Live", BaseUrl = "https://devuat.ncbagroup.com/", ApiKey = "ke123", ApiUser = "sacco", DefaultBankCode = "07", DefaultBranchCode = "000" };

    private static (EquityJengaProvider, FakeHandler) BuildEquity()
    {
        var h = new FakeHandler();
        var settings = new PaymentsSettings(); settings.Banks["EQUITY"] = Equity();
        return (new EquityJengaProvider(new Factory(h), Options.Create(settings), NullLogger<EquityJengaProvider>.Instance), h);
    }
    private static (NcbaProvider, FakeHandler) BuildNcba()
    {
        var h = new FakeHandler();
        var settings = new PaymentsSettings(); settings.Banks["NCBA"] = Ncba();
        return (new NcbaProvider(new Factory(h), Options.Create(settings), NullLogger<NcbaProvider>.Instance), h);
    }

    /// <summary>The mock server's independent decryptor, reproduced here so the envelope is checked against the spec rather than against itself.</summary>
    private static string DecryptJenga(string base64, string apiKey)
    {
        var payload = Convert.FromBase64String(base64);
        var iv = payload[..12]; var salt = payload[12..28]; var tag = payload[^16..]; var cipher = payload[28..^16];
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(apiKey), salt, 65_536, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    [Fact]
    public void Jenga_pin_envelope_matches_the_spec_and_is_fresh_every_time()
    {
        var a = JengaCredentialCipher.Encrypt("2580", "local-api-key");
        var b = JengaCredentialCipher.Encrypt("2580", "local-api-key");
        a.ShouldNotBe(b, "a new IV and salt per request");
        DecryptJenga(a, "local-api-key").ShouldBe("2580");
        Should.Throw<CryptographicException>(() => DecryptJenga(a, "wrong-key"));
    }

    [Fact]
    public async Task Jenga_authenticates_with_api_key_then_submits_c2b_and_b2c_with_encrypted_pin()
    {
        var (p, h) = BuildEquity();
        h.Respond = (req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/authenticate/merchant")) return (HttpStatusCode.OK, """{"accessToken":"jwt-1","refreshToken":"r","expiresIn":"2099-01-01T00:00:00Z","issuedAt":"2026-09-14T00:00:00Z","tokenType":"Bearer"}""");
            return (HttpStatusCode.OK, """{"responseCode":"0","responseDesc":"Request accepted successfully","serviceStatus":"PENDING"}""");
        };
        var col = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-20260914-ABC", "254765555186", 250m, "M00001-FO", "Deposit"), CancellationToken.None);
        col.Accepted.ShouldBeTrue();
        col.ProviderRequestId.ShouldBe("800800_COL20260914ABC", "shortCode_uniqueRequestId, alphanumerics only");
        col.Completed.ShouldBeFalse("Jenga acknowledges then calls back");

        var auth = h.Calls[0];
        auth.Request.Headers.GetValues("Api-Key").Single().ShouldBe("local-api-key");
        JsonDocument.Parse(auth.Body).RootElement.GetProperty("merchantCode").GetString().ShouldBe("0582910862");
        var c2b = h.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/c2b/customer-initiated-payment"));
        c2b.Request.Headers.Authorization!.Parameter.ShouldBe("jwt-1");
        var body = JsonDocument.Parse(c2b.Body).RootElement;
        body.GetProperty("amount").GetString().ShouldBe("250", "amounts are strings on the wire");
        body.GetProperty("msisdn").GetString().ShouldBe("254765555186");
        body.GetProperty("callbackUrl").GetString().ShouldBe("https://api.example/api/payments/webhooks/demo/bank:equity?token=eq-token");
        DecryptJenga(body.GetProperty("pin").GetString()!, "local-api-key").ShouldBe("2580");

        var dis = await p.InitiateDisbursementAsync(new DisbursementRequest(Tenant, "DSB-1", "254765555186", 1_000m, "Withdrawal"), CancellationToken.None);
        dis.Accepted.ShouldBeTrue();
        h.Calls.Count(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/authenticate/merchant")).ShouldBe(1, "token cached until its absolute expiry");
        h.Calls.Last().Request.RequestUri!.AbsolutePath.ShouldEndWith("/b2c/business-to-customer-payment");
    }

    [Fact]
    public async Task Jenga_rejections_status_queries_and_both_callback_shapes_are_handled()
    {
        var (p, h) = BuildEquity();
        h.Respond = (req, body) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/authenticate/merchant")) return (HttpStatusCode.OK, """{"accessToken":"jwt","expiresIn":"2099-01-01T00:00:00Z"}""");
            if (path.EndsWith("/customer-initiated-payment")) return (HttpStatusCode.OK, """{"responseCode":"103","responseDesc":"Internal processing error","serviceStatus":"FAILED"}""");
            if (path.EndsWith("/query-transaction-status"))
                return body.Contains("800800_OK") ? (HttpStatusCode.OK, """{"message":"Your request was successful.","code":"00","metadata":{"requestReference":"800800_OK","transactionId":"DF3801U7FA","status":"SUCCESS"}}""")
                     : body.Contains("800800_WAIT") ? (HttpStatusCode.OK, """{"message":null,"code":"00","metadata":{"requestReference":"800800_WAIT","transactionId":null,"status":"PENDING"}}""")
                     : (HttpStatusCode.OK, """{"message":null,"code":"3011","metadata":{"requestReference":null,"transactionId":null,"status":"FAILED"}}""");
            return (HttpStatusCode.OK, "{}");
        };
        var rejected = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-2", "254700000000", 14m, "x", "y"), CancellationToken.None);
        rejected.Accepted.ShouldBeFalse();
        rejected.FailureReason.ShouldBe("Internal processing error");

        (await p.VerifyTransactionAsync("800800_OK", CancellationToken.None)).ShouldSatisfyAllConditions(s => s.State.ShouldBe(ProviderTransactionState.Succeeded), s => s.ProviderTransactionReference.ShouldBe("DF3801U7FA"));
        (await p.VerifyTransactionAsync("800800_WAIT", CancellationToken.None)).State.ShouldBe(ProviderTransactionState.Pending);
        (await p.VerifyTransactionAsync("800800_NOPE", CancellationToken.None)).State.ShouldBe(ProviderTransactionState.NotFound);

        var headers = new Dictionary<string, string> { ["query:token"] = "eq-token" };
        var success = await p.HandleWebhookAsync(new WebhookPayload("""{"transactionReference":"800800_TXN1","resultType":"SUCCESS","resultCode":"00","resultDesc":"Transaction processed successfully","transactionId":"XQ6390E542"}""", headers, null), CancellationToken.None);
        success.Valid.ShouldBeTrue();
        success.Event!.ProviderRequestId.ShouldBe("800800_TXN1");
        success.Event.ProviderTransactionReference.ShouldBe("XQ6390E542");
        success.Event.State.ShouldBe(ProviderTransactionState.Succeeded);

        var failure = await p.HandleWebhookAsync(new WebhookPayload("""{"code":"3011","message":"Transaction failed","metadata":{"errorMessage":"Insufficient funds","requestReference":"800800_TXN2","status":"FAILED","transactionId":"LD7704D918"}}""", headers, null), CancellationToken.None);
        failure.Event!.ProviderRequestId.ShouldBe("800800_TXN2");
        failure.Event.State.ShouldBe(ProviderTransactionState.Failed);
        failure.Event.FailureReason.ShouldBe("Insufficient funds");
        (await p.HandleWebhookAsync(new WebhookPayload("""{"transactionReference":"x","resultType":"SUCCESS"}""", new Dictionary<string, string>(), null), CancellationToken.None)).Valid.ShouldBeFalse("callback token required");
    }

    [Fact]
    public async Task Ncba_transfer_settles_synchronously_with_the_documented_shape()
    {
        var (p, h) = BuildNcba();
        h.Respond = (_, _) => (HttpStatusCode.OK, """{"TxnReferenceNo":"NCBA20260914120000ABCDEF"}""");
        var r = await p.InitiateDisbursementAsync(new DisbursementRequest(Tenant, "DSB-20260914-XYZ", "11|0123456789|Wanjiru Kamau|001", 2_500.5m, "Withdrawal payout M00001-FO"), CancellationToken.None);
        r.Accepted.ShouldBeTrue();
        r.Completed.ShouldBeTrue("no callback follows");
        r.ProviderTransactionReference.ShouldBe("NCBA20260914120000ABCDEF");
        r.ProviderRequestId!.Length.ShouldBe(12, "NCBA references are at most 12 characters");
        NcbaProvider.ShortReference("DSB-20260914-XYZ").ShouldBe(r.ProviderRequestId, "stable so a retry is a duplicate, not a second payment");

        var call = h.Calls.Single();
        call.Request.Headers.GetValues("API-Key").Single().ShouldBe("ke123");
        call.Request.Headers.GetValues("API-User").Single().ShouldBe("sacco");
        var body = JsonDocument.Parse(call.Body).RootElement;
        body.GetProperty("TranType").GetString().ShouldBe("Pesalink");
        body.GetProperty("BankCode").GetString().ShouldBe("11");
        body.GetProperty("BranchCode").GetString().ShouldBe("001");
        body.GetProperty("Account").GetString().ShouldBe("0123456789");
        body.GetProperty("BeneficiaryName").GetString().ShouldBe("Wanjiru Kamau");
        body.GetProperty("Country").GetString().ShouldBe("Kenya");
        body.GetProperty("Currency").GetString().ShouldBe("KES");
        body.GetProperty("Amount").GetDecimal().ShouldBe(2_500.5m);
        body.GetProperty("Transaction Date").GetString().ShouldMatch(@"^\d{2}-[A-Z][a-z]{2}-\d{4}$");

        var verified = await p.VerifyTransactionAsync(r.ProviderRequestId!, CancellationToken.None);
        verified.State.ShouldBe(ProviderTransactionState.Succeeded);
        verified.ProviderTransactionReference.ShouldBe("NCBA20260914120000ABCDEF");
    }

    [Fact]
    public async Task Ncba_error_codes_mobile_destinations_and_unsupported_collections()
    {
        var (p, h) = BuildNcba();
        h.Respond = (_, _) => (HttpStatusCode.OK, """{"ErrorCode":"12","ErrorDescription":"Insufficient Funds","TxnReferenceNo":null}""");
        var r = await p.InitiateDisbursementAsync(new DisbursementRequest(Tenant, "DSB-2", "254712345678", 100m, "Payout"), CancellationToken.None);
        r.Accepted.ShouldBeFalse();
        r.FailureReason.ShouldBe("NCBA error 12: Insufficient Funds");
        JsonDocument.Parse(h.Calls.Single().Body).RootElement.GetProperty("TranType").GetString().ShouldBe("Mwallet", "a bare 2547… destination is a mobile wallet");
        (await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-1", "254712345678", 10m, "x", "y"), CancellationToken.None)).Accepted.ShouldBeFalse();
        (await p.HandleWebhookAsync(new WebhookPayload("{}", new Dictionary<string, string>(), null), CancellationToken.None)).Valid.ShouldBeFalse();
        (await p.VerifyTransactionAsync("UNKNOWN", CancellationToken.None)).State.ShouldBe(ProviderTransactionState.NotFound);
    }

    [Fact]
    public void Bank_destinations_parse_with_optional_parts()
    {
        BankDestination.Parse("11|0123456789|Jane Doe|001").ShouldBe(new BankDestination("11", "0123456789", "Jane Doe", "001"));
        BankDestination.Parse("11|0123456789").ShouldBe(new BankDestination("11", "0123456789", null, null));
        BankDestination.Parse("0123456789").ShouldBe(new BankDestination(null, "0123456789", null, null));
    }
}
