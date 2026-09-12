using Sacco.Modules.Payments.Domain;
using Sacco.Modules.Payments.Providers;
using Sacco.Shared.Domain;
using Sacco.Shared.Payments;
using Shouldly;

namespace Sacco.UnitTests.Payments;

public class PaymentDomainTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    private static PaymentTransaction Tx() => PaymentTransaction.Create(Guid.NewGuid(), Tenant, PaymentKind.Collection, ProviderNames.MPesa, 500m, "254700100001", null,
        new PaymentPurpose { Type = PaymentPurposeType.SavingsDeposit, AccountNumber = "M00001-FO" }, "COL-1", null, Guid.NewGuid(), Now);

    [Fact]
    public void Transaction_state_machine_is_one_way()
    {
        var tx = Tx();
        tx.Status.ShouldBe(PaymentStatus.Initiated);
        tx.MarkPendingCallback("REQ-1");
        tx.MarkSucceeded("RCPT-1", Guid.NewGuid(), Now);
        tx.IsFinal.ShouldBeTrue();
        Should.Throw<DomainRuleException>(() => tx.MarkFailed("late failure", null, Now)).Code.ShouldBe("payments.already_succeeded");
        tx.MarkTimedOut(Now); // no-op after final
        tx.Status.ShouldBe(PaymentStatus.Succeeded);

        var failed = Tx();
        failed.MarkFailed("Insufficient funds", "RCPT-2", Now);
        Should.Throw<DomainRuleException>(() => failed.MarkSucceeded("RCPT-2", null, Now)).Code.ShouldBe("payments.already_failed");
    }

    [Fact]
    public async Task Sandbox_provider_outcomes_are_driven_by_the_counterparty()
    {
        var p = new SandboxMpesaProvider();
        (await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "R1", "254700100097", 100m, "M00001-FO", "x"), CancellationToken.None)).Accepted.ShouldBeFalse();
        var fail = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "R2", "254700100098", 100m, "M00001-FO", "x"), CancellationToken.None);
        fail.Accepted.ShouldBeTrue();
        (await p.VerifyTransactionAsync(fail.ProviderRequestId!, CancellationToken.None)).State.ShouldBe(ProviderTransactionState.Failed);
        var ok = await p.InitiateCollectionAsync(new CollectionRequest(Tenant, "R3", "254700100012", 100m, "M00001-FO", "x"), CancellationToken.None);
        (await p.VerifyTransactionAsync(ok.ProviderRequestId!, CancellationToken.None)).State.ShouldBe(ProviderTransactionState.Succeeded);
        var callback = p.BuildCallback(ok.ProviderRequestId!)!;
        callback.Result.ShouldBe("Success");
        var parsed = await p.HandleWebhookAsync(new WebhookPayload(System.Text.Json.JsonSerializer.Serialize(callback, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), new Dictionary<string, string>(), null), CancellationToken.None);
        parsed.Valid.ShouldBeTrue();
        parsed.Event!.ProviderRequestId.ShouldBe(ok.ProviderRequestId);
        (await p.HandleWebhookAsync(new WebhookPayload("not json", new Dictionary<string, string>(), null), CancellationToken.None)).Valid.ShouldBeFalse();
    }

    [Fact]
    public async Task Daraja_stk_callback_is_parsed()
    {
        var provider = new DarajaMpesaProvider(new StubHttpClientFactory(), Microsoft.Extensions.Options.Options.Create(new PaymentsSettings()), Microsoft.Extensions.Logging.Abstractions.NullLogger<DarajaMpesaProvider>.Instance);
        const string body = """
        {"Body":{"stkCallback":{"MerchantRequestID":"29115-34620561-1","CheckoutRequestID":"ws_CO_191220191020363925","ResultCode":0,"ResultDesc":"The service request is processed successfully.",
        "CallbackMetadata":{"Item":[{"Name":"Amount","Value":1.00},{"Name":"MpesaReceiptNumber","Value":"NLJ7RT61SV"},{"Name":"TransactionDate","Value":20191219102115},{"Name":"PhoneNumber","Value":254708374149}]}}}}
        """;
        var r = await provider.HandleWebhookAsync(new WebhookPayload(body, new Dictionary<string, string>(), null), CancellationToken.None);
        r.Valid.ShouldBeTrue();
        r.Event!.ProviderTransactionReference.ShouldBe("NLJ7RT61SV");
        r.Event.ProviderRequestId.ShouldBe("ws_CO_191220191020363925");
        r.Event.State.ShouldBe(ProviderTransactionState.Succeeded);
        r.Event.Amount.ShouldBe(1.00m);
        r.Event.PhoneNumber.ShouldBe("254708374149");

        var cancelled = await provider.HandleWebhookAsync(new WebhookPayload("""{"Body":{"stkCallback":{"CheckoutRequestID":"ws_1","ResultCode":1032,"ResultDesc":"Request cancelled by user"}}}""", new Dictionary<string, string>(), null), CancellationToken.None);
        cancelled.Event!.State.ShouldBe(ProviderTransactionState.Failed);
        cancelled.Event.FailureReason.ShouldBe("Request cancelled by user");
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
