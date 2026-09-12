using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Application;
using Sacco.Modules.Payments.Endpoints;
using Sacco.Modules.Payments.Sagas;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Sacco.Shared.Ledger;
using Sacco.Shared.Payments;
using Shouldly;
using Wolverine;

namespace Sacco.IntegrationTests.Payments;

/// <summary>Phase 5 exit criterion: sandbox collection end-to-end (push → webhook → ledger), replay the webhook, no duplicate posting.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PaymentFlowTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private HttpClient Teller => _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Payments.View, Permissions.Payments.Initiate, Permissions.Payments.Reconcile, Permissions.Ledger.View, Permissions.Savings.View, Permissions.Savings.Withdraw, Permissions.Loans.View);
    private HttpClient Manager => _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Savings.WithdrawalApprove, Permissions.Savings.View);
    private HttpClient Webhooks { get { var c = _factory.CreateClient(); return c; } }
    private async Task<LedgerAccountSnapshot> Ledger(string number) => await (await Teller.GetAsync($"/api/ledger/accounts/{number}")).ReadAs<LedgerAccountSnapshot>();
    private static string Phone(string memberNo) => KenyanNames.Members.Single(m => m.MemberNumber == memberNo).Phone;

    private static async Task<(string Message, object Callback)> Simulate(HttpClient c, Guid id, bool? success = null)
    {
        var r = await c.PostAsync($"/api/payments/sandbox/transactions/{id}/callback" + (success is null ? "" : $"?success={success}"), null);
        var body = await r.Content.ReadAsStringAsync();
        r.IsSuccessStatusCode.ShouldBeTrue(body);
        var doc = System.Text.Json.JsonDocument.Parse(body).RootElement;
        return (doc.GetProperty("message").GetString()!, doc.GetProperty("callback"));
    }

    [Theory]
    [InlineData(ProviderNames.MPesa)]
    [InlineData(ProviderNames.AirtelMoney)]
    [InlineData(ProviderNames.SandboxBank)]
    public async Task Collection_posts_once_even_when_the_webhook_is_replayed(string provider)
    {
        var account = LedgerSeeder.FosaAccount("M00001");
        var before = (await Ledger(account)).Balance;

        var tx = await (await Teller.PostAsJsonAsync("/api/payments/collections", new InitiateCollectionRequest(provider, Phone("M00001"), 750m, PaymentPurposeType.SavingsDeposit, account, null, "Top up"))).ReadAs<PaymentTransactionResponse>();
        tx.Status.ShouldBe(PaymentStatus.PendingCallback);
        tx.ProviderRequestId.ShouldNotBeNull();
        (await Ledger(account)).Balance.ShouldBe(before, "nothing is posted until the provider confirms");

        var (_, callback) = await Simulate(Teller, tx.Id);
        var after = await (await Teller.GetAsync($"/api/payments/transactions/{tx.Id}")).ReadAs<PaymentTransactionResponse>();
        after.Status.ShouldBe(PaymentStatus.Succeeded);
        after.ProviderTransactionReference.ShouldNotBeNull();
        after.LedgerJournalEntryId.ShouldNotBeNull();
        (await Ledger(account)).Balance.ShouldBe(before + 750m);

        // Replay the exact same webhook straight at the public callback endpoint (as a provider retry would).
        var routeProvider = provider == ProviderNames.SandboxBank ? "bank:SANDBOX" : provider.ToLowerInvariant();
        var replay = await Webhooks.PostAsync($"/api/payments/webhooks/{DemoTenant.Slug}/{routeProvider}", new StringContent(callback.ToString(), System.Text.Encoding.UTF8, "application/json"));
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayed = await (await Teller.GetAsync($"/api/payments/transactions/{tx.Id}")).ReadAs<PaymentTransactionResponse>();
        replayed.CallbackCount.ShouldBe(2);
        (await Ledger(account)).Balance.ShouldBe(before + 750m, "duplicate delivery must not post again");
        var recon = await (await Teller.GetAsync("/api/ledger/reconciliation")).ReadAs<ReconciliationReport>();
        recon.IsClean.ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_and_rejected_collections_never_touch_the_ledger()
    {
        var account = LedgerSeeder.FosaAccount("M00002");
        var before = (await Ledger(account)).Balance;

        var rejected = await (await Teller.PostAsJsonAsync("/api/payments/collections", new InitiateCollectionRequest(ProviderNames.MPesa, "254700100097", 100m, PaymentPurposeType.SavingsDeposit, account, null, null))).ReadAs<PaymentTransactionResponse>();
        rejected.Status.ShouldBe(PaymentStatus.Failed);

        var failing = await (await Teller.PostAsJsonAsync("/api/payments/collections", new InitiateCollectionRequest(ProviderNames.MPesa, "254700100098", 100m, PaymentPurposeType.SavingsDeposit, account, null, null))).ReadAs<PaymentTransactionResponse>();
        failing.Status.ShouldBe(PaymentStatus.PendingCallback);
        await Simulate(Teller, failing.Id);
        (await (await Teller.GetAsync($"/api/payments/transactions/{failing.Id}")).ReadAs<PaymentTransactionResponse>()).Status.ShouldBe(PaymentStatus.Failed);
        (await Ledger(account)).Balance.ShouldBe(before);
    }

    [Fact]
    public async Task Timeout_verifies_with_the_provider_and_finalises_without_a_callback()
    {
        var account = LedgerSeeder.FosaAccount("M00003");
        var before = (await Ledger(account)).Balance;
        var tx = await (await Teller.PostAsJsonAsync("/api/payments/collections", new InitiateCollectionRequest(ProviderNames.AirtelMoney, Phone("M00003"), 300m, PaymentPurposeType.SavingsDeposit, account, null, null))).ReadAs<PaymentTransactionResponse>();

        // Fire the saga timeout now instead of waiting for the scheduled delivery.
        using var scope = _factory.TenantScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(new CollectionTimeout(tx.Id, DemoTenant.Id, DemoTenant.Slug));

        var after = await (await Teller.GetAsync($"/api/payments/transactions/{tx.Id}")).ReadAs<PaymentTransactionResponse>();
        after.Status.ShouldBe(PaymentStatus.Succeeded, "the sandbox reports success on verification");
        (await Ledger(account)).Balance.ShouldBe(before + 300m);

        // A late duplicate callback after the timeout path is also ignored.
        await Simulate(Teller, tx.Id);
        (await Ledger(account)).Balance.ShouldBe(before + 300m);
    }

    [Fact]
    public async Task Loan_repayment_via_mpesa_reduces_the_loan_balance()
    {
        var loans = await (await Teller.GetAsync("/api/loans?status=Active&pageSize=50")).ReadAs<Sacco.Shared.Http.PagedResult<Sacco.Modules.Lending.Endpoints.LoanListItem>>();
        var loan = loans.Items.First(l => l.MemberId == DemoTenant.MemberId("M00002"));
        var before = (await Ledger(loan.LoanNumber)).Balance;
        var tx = await (await Teller.PostAsJsonAsync("/api/payments/collections", new InitiateCollectionRequest(ProviderNames.MPesa, Phone("M00002"), 1_000m, PaymentPurposeType.LoanRepayment, null, loan.LoanNumber, "Repayment"))).ReadAs<PaymentTransactionResponse>();
        await Simulate(Teller, tx.Id);
        (await (await Teller.GetAsync($"/api/payments/transactions/{tx.Id}")).ReadAs<PaymentTransactionResponse>()).Status.ShouldBe(PaymentStatus.Succeeded);
        (await Ledger(loan.LoanNumber)).Balance.ShouldBeLessThan(before);
    }

    [Fact]
    public async Task Approved_mobile_money_withdrawal_is_paid_out_by_the_disbursement_saga()
    {
        var account = LedgerSeeder.FosaAccount("M00014");
        var w = await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/withdrawals", new WithdrawalRequestDto(1_200m, PayoutChannel.MPesa, Phone("M00014"), "To M-Pesa"))).ReadAs<WithdrawalResponse>();
        await (await Manager.PostAsync($"/api/savings/withdrawals/{w.Id}/approve", null)).ReadAs<WithdrawalResponse>();
        var before = await Ledger(account);

        var tx = await (await Teller.PostAsJsonAsync("/api/payments/disbursements", new InitiateDisbursementRequest(w.Id, null))).ReadAs<PaymentTransactionResponse>();
        tx.Kind.ShouldBe(PaymentKind.Disbursement);
        tx.Status.ShouldBe(PaymentStatus.PendingCallback);
        (await Teller.PostAsJsonAsync("/api/payments/disbursements", new InitiateDisbursementRequest(w.Id, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await Simulate(Teller, tx.Id);
        (await (await Teller.GetAsync($"/api/payments/transactions/{tx.Id}")).ReadAs<PaymentTransactionResponse>()).Status.ShouldBe(PaymentStatus.Succeeded);
        var paid = await (await Teller.GetAsync($"/api/savings/withdrawals/{w.Id}")).ReadAs<WithdrawalResponse>();
        paid.Status.ShouldBe(WithdrawalStatus.Paid);
        var after = await Ledger(account);
        after.Balance.ShouldBe(before.Balance - 1_250m); // amount + 50 fee
        after.HeldAmount.ShouldBe(before.HeldAmount - 1_250m);
    }

    [Fact]
    public async Task Unsolicited_c2b_credit_with_an_account_reference_is_applied_and_unknown_ones_park_in_suspense()
    {
        var account = LedgerSeeder.FosaAccount("M00005");
        var before = (await Ledger(account)).Balance;
        var suspenseBefore = (await (await Teller.GetAsync("/api/ledger/gl-accounts/2500")).ReadAs<Sacco.Modules.Ledger.Endpoints.GlAccountResponse>()).Balance;
        var reference = $"C2B{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var body = $$"""{"providerTransactionReference":"{{reference}}","result":"Success","amount":420,"phoneNumber":"{{Phone("M00005")}}","accountReference":"{{account}}"}""";
        (await Webhooks.PostAsync($"/api/payments/webhooks/{DemoTenant.Slug}/mpesa", new StringContent(body, System.Text.Encoding.UTF8, "application/json"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Ledger(account)).Balance.ShouldBe(before + 420m);
        (await Webhooks.PostAsync($"/api/payments/webhooks/{DemoTenant.Slug}/mpesa", new StringContent(body, System.Text.Encoding.UTF8, "application/json"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Ledger(account)).Balance.ShouldBe(before + 420m);

        var unknown = $$"""{"providerTransactionReference":"{{reference}}X","result":"Success","amount":99,"phoneNumber":"254700100099","accountReference":"NOSUCH-ACCT"}""";
        (await Webhooks.PostAsync($"/api/payments/webhooks/{DemoTenant.Slug}/mpesa", new StringContent(unknown, System.Text.Encoding.UTF8, "application/json"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var suspenseAfter = (await (await Teller.GetAsync("/api/ledger/gl-accounts/2500")).ReadAs<Sacco.Modules.Ledger.Endpoints.GlAccountResponse>()).Balance;
        (suspenseAfter - suspenseBefore).ShouldBe(99m);

        (await Webhooks.PostAsync($"/api/payments/webhooks/nosuchtenant/mpesa", new StringContent(body, System.Text.Encoding.UTF8, "application/json"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Seeded_fixtures_cover_every_outcome_per_provider()
    {
        var all = await (await Teller.GetAsync("/api/payments/transactions?pageSize=200")).ReadAs<Sacco.Shared.Http.PagedResult<PaymentTransactionResponse>>();
        foreach (var provider in new[] { ProviderNames.MPesa, ProviderNames.AirtelMoney, ProviderNames.SandboxBank })
        {
            all.Items.ShouldContain(t => t.Provider == provider && t.Status == PaymentStatus.Succeeded && t.CallbackCount == 2, $"{provider}: success with an ignored duplicate delivery");
            all.Items.ShouldContain(t => t.Provider == provider && t.Status == PaymentStatus.Failed);
            all.Items.ShouldContain(t => t.Provider == provider && t.Status == PaymentStatus.TimedOut);
        }
        all.Items.ShouldContain(t => t.Kind == PaymentKind.Disbursement && t.Status == PaymentStatus.Succeeded);
    }
}
