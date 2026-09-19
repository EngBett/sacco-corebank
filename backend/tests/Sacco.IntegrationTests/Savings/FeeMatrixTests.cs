using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Savings.Application;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;
using Shouldly;

namespace Sacco.IntegrationTests.Savings;

/// <summary>The fee matrix (ADR 0015): maker-checker rule maintenance and fees actually landing on deposits and withdrawals.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class FeeMatrixTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private HttpClient Accountant => _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Savings.View, Permissions.Savings.FeesManage);
    private HttpClient Manager => _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Savings.View, Permissions.Savings.FeesApprove, Permissions.Savings.WithdrawalApprove);
    private HttpClient Teller => _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Savings.View, Permissions.Savings.Deposit, Permissions.Savings.Withdraw, Permissions.Ledger.View);

    private static CreateFeeRuleCommand Fixed(FeeTransactionType type, FeeChannel? channel, string? product, decimal min, decimal? max, decimal amount, Guid? supersedes = null) =>
        new(type, channel, product, min, max, FeeChargeType.Fixed, amount, null, null, null, null, Coa.FosaFeesAndCharges, supersedes);

    private async Task<FeeRuleResponse> ProposeAndApprove(CreateFeeRuleCommand command)
    {
        var created = await (await Accountant.PostAsJsonAsync("/api/savings/fees", command)).ReadAs<FeeRuleResponse>();
        created.Status.ShouldBe(FeeRuleStatus.PendingApproval);
        return await (await Manager.PostAsync($"/api/savings/fees/{created.Id}/approve", null)).ReadAs<FeeRuleResponse>();
    }

    [Fact]
    public async Task A_proposed_rule_needs_a_different_approver_and_the_income_gl_is_validated()
    {
        var created = await (await Accountant.PostAsJsonAsync("/api/savings/fees", Fixed(FeeTransactionType.Withdrawal, FeeChannel.Cash, SavingsSeeder.Shares, 1m, 10m, 1m))).ReadAs<FeeRuleResponse>();
        (await Accountant.PostAsync($"/api/savings/fees/{created.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the maker lacks the checker permission");

        var both = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Savings.FeesManage, Permissions.Savings.FeesApprove);
        var self = await both.PostAsync($"/api/savings/fees/{created.Id}/approve", null);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await self.Content.ReadAsStringAsync()).ShouldContain("maker_checker.same_user");
        (await (await Manager.PostAsJsonAsync($"/api/savings/fees/{created.Id}/reject", new ReasonDto("Shares are not withdrawable"))).ReadAs<FeeRuleResponse>()).Status.ShouldBe(FeeRuleStatus.Rejected);

        var badGl = await Accountant.PostAsJsonAsync("/api/savings/fees", Fixed(FeeTransactionType.Deposit, FeeChannel.Cash, null, 1m, null, 5m) with { FeeIncomeGlAccountCode = Coa.FosaSavingsControl });
        badGl.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await badGl.Content.ReadAsStringAsync()).ShouldContain("savings.fees.gl_not_income");
    }

    [Fact]
    public async Task Seeded_tariffs_price_withdrawals_with_bands_and_caps()
    {
        async Task<decimal> Quote(FeeChannel channel, decimal amount) =>
            (await (await Teller.PostAsJsonAsync("/api/savings/fees/quote", new FeeQuoteRequest(FeeTransactionType.Withdrawal, channel, SavingsSeeder.FosaCurrent, amount))).ReadAs<FeeQuoteResponse>()).Fee;

        (await Quote(FeeChannel.MPesa, 800m)).ShouldBe(30m);
        (await Quote(FeeChannel.MPesa, 6_000m)).ShouldBe(75m);
        (await Quote(FeeChannel.BankTransfer, 1_000m)).ShouldBe(50m, "0.5% is 5, raised to the 50 minimum");
        (await Quote(FeeChannel.BankTransfer, 200_000m)).ShouldBe(500m, "0.5% is 1,000, capped at 500");
        (await Quote(FeeChannel.Cash, 1_000m)).ShouldBe(50m, "no cash rule — the product's own withdrawal fee applies");

        var account = LedgerSeeder.FosaAccount("M00015");
        await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/deposits", new DepositRequest(7_000m, DepositChannel.Cash, $"TOP-{Guid.NewGuid():N}"[..20], "headroom"))).ReadAs<DepositResult>();
        var w = await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/withdrawals", new WithdrawalRequestDto(6_000m, PayoutChannel.MPesa, "254700100015", "Tariff check"))).ReadAs<WithdrawalResponse>();
        w.Fee.ShouldBe(75m, "the fee is fixed on the request when it is made");
        await (await Manager.PostAsJsonAsync($"/api/savings/withdrawals/{w.Id}/reject", new ReasonDto("test cleanup"))).ReadAs<WithdrawalResponse>();
    }

    [Fact]
    public async Task A_deposit_fee_comes_off_the_deposit_in_the_same_journal()
    {
        // An amount no other test deposits, so this live rule can't leak into their balances.
        var rule = await ProposeAndApprove(Fixed(FeeTransactionType.Deposit, FeeChannel.Cash, null, 7_777m, 7_777.99m, 77m));
        try
        {
            var account = LedgerSeeder.FosaAccount("M00012");
            var before = (await (await Teller.GetAsync($"/api/ledger/accounts/{account}")).ReadAs<LedgerAccountSnapshot>()).Balance;
            var result = await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/deposits", new DepositRequest(7_777m, DepositChannel.Cash, $"FEE-{Guid.NewGuid():N}"[..20], "fee test"))).ReadAs<DepositResult>();
            result.Fee.ShouldBe(77m);
            result.NewBalance.ShouldBe(before + 7_700m);

            var recon = await (await Teller.GetAsync("/api/ledger/reconciliation")).ReadAs<Sacco.Modules.Ledger.Application.ReconciliationReport>();
            recon.IsClean.ShouldBeTrue();
        }
        finally
        {
            await (await Manager.PostAsync($"/api/savings/fees/{rule.Id}/deactivate", null)).ReadAs<FeeRuleResponse>();
        }
    }

    [Fact]
    public async Task Overlapping_rules_are_refused_and_a_revision_replaces_the_rule_it_supersedes()
    {
        var original = await ProposeAndApprove(Fixed(FeeTransactionType.Withdrawal, FeeChannel.AirtelMoney, SavingsSeeder.BosaDeposit, 1m, 100m, 5m));

        var overlapping = await (await Accountant.PostAsJsonAsync("/api/savings/fees", Fixed(FeeTransactionType.Withdrawal, FeeChannel.AirtelMoney, SavingsSeeder.BosaDeposit, 50m, 500m, 9m))).ReadAs<FeeRuleResponse>();
        var refused = await Manager.PostAsync($"/api/savings/fees/{overlapping.Id}/approve", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("savings.fees.overlap");
        await (await Manager.PostAsJsonAsync($"/api/savings/fees/{overlapping.Id}/reject", new ReasonDto("overlaps"))).ReadAs<FeeRuleResponse>();

        var revision = await ProposeAndApprove(Fixed(FeeTransactionType.Withdrawal, FeeChannel.AirtelMoney, SavingsSeeder.BosaDeposit, 1m, 100m, 7m, supersedes: original.Id));
        revision.Status.ShouldBe(FeeRuleStatus.Active);
        (await (await Manager.GetAsync($"/api/savings/fees/{original.Id}")).ReadAs<FeeRuleResponse>()).Status.ShouldBe(FeeRuleStatus.Inactive);

        var quote = await (await Teller.PostAsJsonAsync("/api/savings/fees/quote", new FeeQuoteRequest(FeeTransactionType.Withdrawal, FeeChannel.AirtelMoney, SavingsSeeder.BosaDeposit, 80m))).ReadAs<FeeQuoteResponse>();
        quote.Fee.ShouldBe(7m);
        quote.RuleId.ShouldBe(revision.Id);

        await (await Manager.PostAsync($"/api/savings/fees/{revision.Id}/deactivate", null)).ReadAs<FeeRuleResponse>();
    }
}
