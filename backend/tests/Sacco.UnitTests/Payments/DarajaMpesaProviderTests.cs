using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sacco.Modules.Payments.Providers;
using Sacco.Shared.Payments;
using Shouldly;

namespace Sacco.UnitTests.Payments;

/// <summary>Drives the live Daraja provider through a fake HTTP handler: STK/B2C request shapes, tolerant response parsing, status query and callbacks.</summary>
public sealed class DarajaMpesaProviderTests
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

    private static (DarajaMpesaProvider Provider, FakeHandler Handler) Build()
    {
        var handler = new FakeHandler();
        var s = new ProviderSettings
        {
            Mode = "Live", BaseUrl = "https://sandbox.safaricom.co.ke/", ConsumerKey = "key", ConsumerSecret = "secret", ShortCode = "174379", Passkey = "pass",
            InitiatorName = "testapi", SecurityCredential = "cred", CallbackBaseUrl = "https://api.example.test/api/payments/webhooks/demo", StkQueryPath = "mpesa/stkpushquery/v2/query",
        };
        return (new DarajaMpesaProvider(new Factory(handler), Options.Create(new PaymentsSettings { MPesa = s }), NullLogger<DarajaMpesaProvider>.Instance), handler);
    }

    private const string TokenJson = """{"access_token":"tok-1","expires_in":"3599"}""";
    private static bool IsToken(HttpRequestMessage r) => r.RequestUri!.AbsolutePath.EndsWith("/oauth/v1/generate");

    [Theory]
    [InlineData("""{"MerchantRequestID":"MR1","CheckoutRequestID":"ws_CO_1","ResponseCode":"0","ResponseDescription":"Success. Request accepted for processing","CustomerMessage":"Success"}""")]
    [InlineData("""{"merchantRequestID":"MR1","checkoutRequestID":"ws_CO_1","responseCode":"0","responseDescription":"Success. Request accepted for processing","customerMessage":"Success"}""")]
    public async Task Stk_push_sends_the_documented_shape_and_reads_the_checkout_id_regardless_of_casing(string response)
    {
        var (p, h) = Build();
        h.Respond = (req, _) => IsToken(req) ? (HttpStatusCode.OK, TokenJson) : (HttpStatusCode.OK, response);

        var result = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-1", "254722000001", 300.20m, "M00004-FO", "Deposit"), CancellationToken.None);
        result.Accepted.ShouldBeTrue(result.FailureReason);
        result.ProviderRequestId.ShouldBe("ws_CO_1");

        var token = h.Calls.Single(c => IsToken(c.Request));
        token.Request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(token.Request.Headers.Authorization.Parameter!)).ShouldBe("key:secret");

        var push = h.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/mpesa/stkpush/v1/processrequest"));
        push.Request.Headers.Authorization!.Parameter.ShouldBe("tok-1");
        var body = JsonDocument.Parse(push.Body).RootElement;
        body.GetProperty("BusinessShortCode").GetString().ShouldBe("174379");
        body.GetProperty("TransactionType").GetString().ShouldBe("CustomerPayBillOnline");
        body.GetProperty("Amount").GetInt32().ShouldBe(301, "Daraja takes whole shillings; never under-collect");
        body.GetProperty("PhoneNumber").GetString().ShouldBe("254722000001");
        body.GetProperty("CallBackURL").GetString().ShouldBe("https://api.example.test/api/payments/webhooks/demo/mpesa");
        var ts = body.GetProperty("Timestamp").GetString()!;
        Encoding.UTF8.GetString(Convert.FromBase64String(body.GetProperty("Password").GetString()!)).ShouldBe("174379pass" + ts);
    }

    [Fact]
    public async Task Stk_push_is_rejected_on_error_bodies_and_non_zero_response_codes()
    {
        var (p, h) = Build();
        h.Respond = (req, _) => IsToken(req) ? (HttpStatusCode.OK, TokenJson) : (HttpStatusCode.BadRequest, """{"requestId":"r1","errorCode":"400.002.02","errorMessage":"Bad Request - Invalid PhoneNumber"}""");
        var bad = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-2", "0722", 10m, "M00004-FO", "x"), CancellationToken.None);
        bad.Accepted.ShouldBeFalse();
        bad.FailureReason.ShouldBe("Bad Request - Invalid PhoneNumber");

        h.Respond = (req, _) => IsToken(req) ? (HttpStatusCode.OK, TokenJson) : (HttpStatusCode.OK, """{"ResponseCode":"1","ResponseDescription":"Merchant does not exist"}""");
        var declined = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "COL-3", "254722000001", 10m, "M00004-FO", "x"), CancellationToken.None);
        declined.Accepted.ShouldBeFalse();
        declined.FailureReason.ShouldBe("Merchant does not exist");
    }

    [Fact]
    public async Task B2c_sends_initiator_credentials_and_reads_the_conversation_id()
    {
        var (p, h) = Build();
        h.Respond = (req, _) => IsToken(req) ? (HttpStatusCode.OK, TokenJson) : (HttpStatusCode.OK, """{"conversationID":"AG_1","originatorConversationID":"o1","responseCode":"0","responseDescription":"Accept the service request successfully."}""");
        var result = await p.InitiateDisbursementAsync(new DisbursementRequest(Tenant, "WD-1", "254722000001", 200m, "Withdrawal"), CancellationToken.None);
        result.Accepted.ShouldBeTrue(result.FailureReason);
        result.ProviderRequestId.ShouldBe("AG_1");
        result.Completed.ShouldBeFalse("B2C settles through the Result callback");
        var body = JsonDocument.Parse(h.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/mpesa/b2c/v1/paymentrequest")).Body).RootElement;
        body.GetProperty("InitiatorName").GetString().ShouldBe("testapi");
        body.GetProperty("SecurityCredential").GetString().ShouldBe("cred");
        body.GetProperty("CommandID").GetString().ShouldBe("BusinessPayment");
        body.GetProperty("PartyB").GetString().ShouldBe("254722000001");
        body.GetProperty("ResultURL").GetString().ShouldBe("https://api.example.test/api/payments/webhooks/demo/mpesa");
    }

    [Fact]
    public async Task Status_query_maps_result_codes()
    {
        var (p, h) = Build();
        h.Respond = (req, _) => IsToken(req) ? (HttpStatusCode.OK, TokenJson) : (HttpStatusCode.OK, """{"ResponseCode":"0","ResultCode":"0","ResultDesc":"The service request is processed successfully.","MpesaReceiptNumber":"RKT1"}""");
        var ok = await p.VerifyTransactionAsync("ws_CO_1", CancellationToken.None);
        ok.State.ShouldBe(ProviderTransactionState.Succeeded);
        ok.ProviderTransactionReference.ShouldBe("RKT1");
        h.Calls.Single(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/mpesa/stkpushquery/v2/query")).Body.ShouldContain("\"CheckoutRequestID\":\"ws_CO_1\"");

        h.Respond = (req, _) => IsToken(req) ? (HttpStatusCode.OK, TokenJson) : (HttpStatusCode.OK, """{"ResponseCode":"0","ResultCode":1032,"ResultDesc":"Request cancelled by user"}""");
        var cancelled = await p.VerifyTransactionAsync("ws_CO_2", CancellationToken.None);
        cancelled.State.ShouldBe(ProviderTransactionState.Failed);
        cancelled.FailureReason.ShouldBe("Request cancelled by user");

        h.Respond = (req, _) => IsToken(req) ? (HttpStatusCode.OK, TokenJson) : (HttpStatusCode.OK, """{"ResponseCode":"0","ResponseDescription":"still processing"}""");
        (await p.VerifyTransactionAsync("ws_CO_3", CancellationToken.None)).State.ShouldBe(ProviderTransactionState.Pending);
    }

    [Fact]
    public async Task Stk_callback_yields_receipt_amount_and_phone_and_failures_carry_the_reason()
    {
        var (p, _) = Build();
        var success = """{"Body":{"stkCallback":{"MerchantRequestID":"MR1","CheckoutRequestID":"ws_CO_1","ResultCode":0,"ResultDesc":"ok","CallbackMetadata":{"Item":[{"Name":"Amount","Value":300},{"Name":"MpesaReceiptNumber","Value":"RKT1XYZ"},{"Name":"Balance"},{"Name":"TransactionDate","Value":20260914120000},{"Name":"PhoneNumber","Value":254722000001}]}}}}""";
        var r = await p.HandleWebhookAsync(new WebhookPayload(success, new Dictionary<string, string>(), null), CancellationToken.None);
        r.Event.ShouldNotBeNull();
        r.Event.State.ShouldBe(ProviderTransactionState.Succeeded);
        r.Event.ProviderRequestId.ShouldBe("ws_CO_1");
        r.Event.ProviderTransactionReference.ShouldBe("RKT1XYZ");
        r.Event.Amount.ShouldBe(300m);
        r.Event.PhoneNumber.ShouldBe("254722000001");

        var failed = """{"Body":{"stkCallback":{"MerchantRequestID":"MR2","CheckoutRequestID":"ws_CO_2","ResultCode":1032,"ResultDesc":"Request cancelled by user"}}}""";
        var f = await p.HandleWebhookAsync(new WebhookPayload(failed, new Dictionary<string, string>(), null), CancellationToken.None);
        f.Event!.State.ShouldBe(ProviderTransactionState.Failed);
        f.Event.FailureReason.ShouldBe("Request cancelled by user");
        f.Event.ProviderTransactionReference.ShouldBe("STK-ws_CO_2", "a failed push still needs a unique reference for idempotency");

        var b2c = """{"Result":{"ResultType":0,"ResultCode":0,"ResultDesc":"ok","OriginatorConversationID":"o1","ConversationID":"AG_1","TransactionID":"RKT2B2C","ResultParameters":{"ResultParameter":[{"Key":"TransactionAmount","Value":200}]}}}""";
        var b = await p.HandleWebhookAsync(new WebhookPayload(b2c, new Dictionary<string, string>(), null), CancellationToken.None);
        b.Event!.State.ShouldBe(ProviderTransactionState.Succeeded);
        b.Event.ProviderRequestId.ShouldBe("AG_1");
        b.Event.ProviderTransactionReference.ShouldBe("RKT2B2C");

        (await p.HandleWebhookAsync(new WebhookPayload("""{"hello":"world"}""", new Dictionary<string, string>(), null), CancellationToken.None)).Event.ShouldBeNull();
        (await p.HandleWebhookAsync(new WebhookPayload("""{"Body":{"stkCallback":{"ResultDesc":"no ids"}}}""", new Dictionary<string, string>(), null), CancellationToken.None)).Event.ShouldBeNull();
    }
}
