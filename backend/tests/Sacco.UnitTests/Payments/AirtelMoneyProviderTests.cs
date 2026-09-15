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

/// <summary>Drives the live Airtel Money provider through a fake HTTP handler: request shapes, headers, token caching, status mapping, callback validation.</summary>
public sealed class AirtelMoneyProviderTests
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

    private static (AirtelMoneyProvider Provider, FakeHandler Handler) Build(ProviderSettings? settings = null)
    {
        var handler = new FakeHandler();
        var s = settings ?? new ProviderSettings { Mode = "Live", BaseUrl = "https://openapiuat.airtel.africa/", ClientId = "id", ClientSecret = "secret", Country = "KE", Currency = "KES" };
        var provider = new AirtelMoneyProvider(new Factory(handler), Options.Create(new PaymentsSettings { AirtelMoney = s }), NullLogger<AirtelMoneyProvider>.Instance);
        return (provider, handler);
    }

    private static string TokenJson => """{"access_token":"tok-1","expires_in":180,"token_type":"bearer"}""";

    [Fact]
    public async Task Ussd_push_sends_the_documented_shape_with_country_and_currency_headers_and_caches_the_token()
    {
        var (p, h) = Build();
        h.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/auth/oauth2/token")
            ? (HttpStatusCode.OK, TokenJson)
            : (HttpStatusCode.OK, """{"data":{"transaction":{"id":"COL-1","status":"SUCCESS"}},"status":{"code":"200","message":"SUCCESS","success":true}}""");

        var result = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-1", "254733123456", 250.40m, "M00001-FO", "Deposit to M00001-FO"), CancellationToken.None);
        result.Accepted.ShouldBeTrue();
        result.ProviderRequestId.ShouldBe("COL-1");

        var push = h.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/merchant/v1/payments/"));
        push.Request.Headers.Authorization!.Parameter.ShouldBe("tok-1");
        push.Request.Headers.GetValues("X-Country").Single().ShouldBe("KE");
        push.Request.Headers.GetValues("X-Currency").Single().ShouldBe("KES");
        var body = JsonDocument.Parse(push.Body).RootElement;
        body.GetProperty("subscriber").GetProperty("msisdn").GetString().ShouldBe("733123456", "national number, no country code");
        body.GetProperty("transaction").GetProperty("amount").GetInt32().ShouldBe(251, "whole shillings, rounded up");
        body.GetProperty("transaction").GetProperty("id").GetString().ShouldBe("COL-1");
        JsonDocument.Parse(h.Calls[0].Body).RootElement.GetProperty("grant_type").GetString().ShouldBe("client_credentials");

        await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-2", "0733123456", 10m, "x", "y"), CancellationToken.None);
        h.Calls.Count(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/auth/oauth2/token")).ShouldBe(1, "the token is cached until it nears expiry");
    }

    [Fact]
    public async Task Provider_rejections_and_transport_failures_are_reported_not_thrown()
    {
        var (p, h) = Build();
        h.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/token")
            ? (HttpStatusCode.OK, TokenJson)
            : (HttpStatusCode.OK, """{"status":{"code":"400","message":"Invalid MSISDN","success":false}}""");
        var rejected = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-3", "254700000000", 5m, "x", "y"), CancellationToken.None);
        rejected.Accepted.ShouldBeFalse();
        rejected.FailureReason.ShouldBe("Invalid MSISDN");

        var (p2, h2) = Build();
        h2.Respond = (_, _) => (HttpStatusCode.ServiceUnavailable, "");
        var down = await p2.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-4", "254700000000", 5m, "x", "y"), CancellationToken.None);
        down.Accepted.ShouldBeFalse();
        down.FailureReason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Disbursement_encrypts_the_pin_under_the_public_key()
    {
        using var rsa = RSA.Create(2048);
        var settings = new ProviderSettings { Mode = "Live", BaseUrl = "https://openapiuat.airtel.africa/", ClientId = "id", ClientSecret = "s", DisbursementPin = "4321", DisbursementPublicKey = rsa.ToXmlString(false) };
        var (p, h) = Build(settings);
        h.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/token")
            ? (HttpStatusCode.OK, TokenJson)
            : (HttpStatusCode.OK, """{"data":{"transaction":{"id":"DIS-1","airtel_money_id":"AM123","reference_id":"R","status":"TIP"}},"status":{"success":true,"message":"SUCCESS"}}""");
        var r = await p.InitiateDisbursementAsync(new DisbursementRequest(Tenant, "DIS-1", "254733123456", 1_000m, "Withdrawal M00001"), CancellationToken.None);
        r.Accepted.ShouldBeTrue();
        r.ProviderRequestId.ShouldBe("DIS-1");
        var body = JsonDocument.Parse(h.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/standard/v2/disbursements/")).Body).RootElement;
        body.GetProperty("transaction").GetProperty("type").GetString().ShouldBe("B2C");
        body.GetProperty("payee").GetProperty("msisdn").GetString().ShouldBe("733123456");
        var encrypted = Convert.FromBase64String(body.GetProperty("pin").GetString()!);
        Encoding.UTF8.GetString(rsa.Decrypt(encrypted, RSAEncryptionPadding.Pkcs1)).ShouldBe("4321");
        body.TryGetProperty("PIN", out _).ShouldBeFalse("snake_case keys only");

        // PEM public keys work too.
        AirtelMoneyProvider.EncryptPin("1111", rsa.ExportSubjectPublicKeyInfoPem()).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Enquiry_maps_ts_tf_tip_and_falls_through_to_disbursements()
    {
        var (p, h) = Build();
        h.Respond = (req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/token")) return (HttpStatusCode.OK, TokenJson);
            if (path.Contains("/standard/v1/payments/COL-OK")) return (HttpStatusCode.OK, """{"data":{"transaction":{"id":"COL-OK","status":"TS","airtel_money_id":"AM777","message":"ok"}},"status":{"success":true}}""");
            if (path.Contains("/standard/v1/payments/COL-BAD")) return (HttpStatusCode.OK, """{"data":{"transaction":{"id":"COL-BAD","status":"TF","message":"Insufficient balance"}},"status":{"success":true}}""");
            if (path.Contains("/standard/v1/payments/")) return (HttpStatusCode.NotFound, """{"status":{"success":false,"message":"Transaction not found"}}""");
            if (path.Contains("/standard/v2/disbursements/DIS-1")) return (HttpStatusCode.OK, """{"data":{"transaction":{"id":"DIS-1","status":"TIP"}},"status":{"success":true}}""");
            return (HttpStatusCode.NotFound, "{}");
        };
        var ok = await p.VerifyTransactionAsync("COL-OK", CancellationToken.None);
        ok.State.ShouldBe(ProviderTransactionState.Succeeded);
        ok.ProviderTransactionReference.ShouldBe("AM777");
        var bad = await p.VerifyTransactionAsync("COL-BAD", CancellationToken.None);
        bad.State.ShouldBe(ProviderTransactionState.Failed);
        bad.FailureReason.ShouldBe("Insufficient balance");
        (await p.VerifyTransactionAsync("DIS-1", CancellationToken.None)).State.ShouldBe(ProviderTransactionState.Pending);
        (await p.VerifyTransactionAsync("NOPE", CancellationToken.None)).State.ShouldBe(ProviderTransactionState.NotFound);
    }

    [Fact]
    public async Task Callbacks_are_parsed_and_the_url_token_is_enforced()
    {
        var (p, _) = Build(new ProviderSettings { Mode = "Live", BaseUrl = "https://x/", CallbackToken = "s3cret" });
        const string success = """{"transaction":{"id":"COL-1","message":"Paid","status_code":"TS","airtel_money_id":"AM999"},"hash":"abc"}""";
        var noToken = await p.HandleWebhookAsync(new WebhookPayload(success, new Dictionary<string, string>(), null), CancellationToken.None);
        noToken.Valid.ShouldBeFalse();
        var withToken = await p.HandleWebhookAsync(new WebhookPayload(success, new Dictionary<string, string> { ["query:token"] = "s3cret" }, null), CancellationToken.None);
        withToken.Valid.ShouldBeTrue();
        withToken.Event!.ProviderRequestId.ShouldBe("COL-1");
        withToken.Event.ProviderTransactionReference.ShouldBe("AM999");
        withToken.Event.State.ShouldBe(ProviderTransactionState.Succeeded);

        var failed = await p.HandleWebhookAsync(new WebhookPayload("""{"transaction":{"id":"COL-2","message":"Insufficient funds","status_code":"TF"}}""", new Dictionary<string, string> { ["query:token"] = "s3cret" }, null), CancellationToken.None);
        failed.Event!.State.ShouldBe(ProviderTransactionState.Failed);
        failed.Event.FailureReason.ShouldBe("Insufficient funds");
        failed.Event.ProviderTransactionReference.ShouldBe("AM-COL-2", "no receipt on failure: the id still makes the event idempotent");

        var inProgress = await p.HandleWebhookAsync(new WebhookPayload("""{"transaction":{"id":"COL-3","status_code":"TIP"}}""", new Dictionary<string, string> { ["query:token"] = "s3cret" }, null), CancellationToken.None);
        inProgress.Valid.ShouldBeFalse("an in-progress callback must not be finalised; Airtel retries with the final status");
        (await p.HandleWebhookAsync(new WebhookPayload("not json", new Dictionary<string, string> { ["query:token"] = "s3cret" }, null), CancellationToken.None)).Valid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("254733123456", "733123456")] [InlineData("0733123456", "733123456")] [InlineData("+254 733 123 456", "733123456")] [InlineData("733123456", "733123456")]
    public void Msisdns_are_sent_as_national_numbers(string input, string expected) => AirtelMoneyProvider.NationalMsisdn(input).ShouldBe(expected);
}
