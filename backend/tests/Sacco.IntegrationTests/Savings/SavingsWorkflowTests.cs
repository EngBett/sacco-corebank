using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Application;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;
using Shouldly;

namespace Sacco.IntegrationTests.Savings;

/// <summary>Phase 3 exit criterion: open a savings account, deposit, request a withdrawal (notice rules), see FOSA vs BOSA correctly reflected in the ledger.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SavingsWorkflowTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private HttpClient Teller => _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Savings.View, Permissions.Savings.Deposit, Permissions.Savings.Withdraw, Permissions.Savings.AccountsOpen, Permissions.Ledger.View);
    private HttpClient Manager => _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Savings.View, Permissions.Savings.WithdrawalApprove, Permissions.Savings.DividendsApprove, Permissions.Savings.AccountsOpen, Permissions.Ledger.View);
    private HttpClient Accountant => _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Savings.View, Permissions.Savings.DividendsDeclare, Permissions.Savings.ProductsManage, Permissions.Ledger.View);

    private async Task<LedgerAccountSnapshot> Ledger(string number) => await (await Teller.GetAsync($"/api/ledger/accounts/{number}")).ReadAs<LedgerAccountSnapshot>();
    private async Task<decimal> Gl(string code) => (await (await Teller.GetAsync($"/api/ledger/gl-accounts/{code}")).ReadAs<Sacco.Modules.Ledger.Endpoints.GlAccountResponse>()).Balance;

    [Fact]
    public async Task Cannot_open_account_for_member_who_is_not_in_good_standing()
    {
        var pending = DemoTenant.MemberId("M00017");
        var r = await Manager.PostAsJsonAsync("/api/savings/accounts", new OpenAccountRequest(pending, SavingsSeeder.FosaCurrent));
        r.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync()).ShouldContain("savings.member_not_in_good_standing");
    }

    [Fact]
    public async Task Bosa_deposit_via_teller_cash_lands_in_bosa_with_clearing_lines_and_fosa_cash_up()
    {
        var account = LedgerSeeder.SavingsAccount("M00002");
        var before = await Ledger(account);
        var cashBefore = await Gl("1010");
        var dueFromBefore = await Gl("1800");

        var result = await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/deposits", new DepositRequest(2_500m, DepositChannel.Cash, $"RCPT-{Guid.NewGuid():N}"[..20], "Monthly contribution"))).ReadAs<DepositResult>();
        result.NewBalance.ShouldBe(before.Balance + 2_500m);

        (await Ledger(account)).Segment.ShouldBe(Segment.Bosa);
        (await Gl("1010") - cashBefore).ShouldBe(2_500m);      // FOSA teller cash
        (await Gl("1800") - dueFromBefore).ShouldBe(2_500m);   // BOSA due from FOSA

        var journal = await (await Teller.GetAsync($"/api/ledger/journals/{result.JournalEntryId}")).ReadAs<Sacco.Modules.Ledger.Endpoints.JournalResponse>();
        journal.Lines.Count.ShouldBe(4);
        journal.Lines.Where(l => l.Segment == Segment.Fosa).Sum(l => l.Direction == EntryDirection.Debit ? l.Amount : -l.Amount).ShouldBe(0m);
        journal.Lines.Where(l => l.Segment == Segment.Bosa).Sum(l => l.Direction == EntryDirection.Debit ? l.Amount : -l.Amount).ShouldBe(0m);

        // Same receipt twice → idempotent conflict, no double posting.
        var dup = await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/deposits", new DepositRequest(2_500m, DepositChannel.Cash, result.JournalReference, "again"));
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Fosa_cash_withdrawal_within_teller_limit_is_paid_immediately_with_fee()
    {
        var account = LedgerSeeder.FosaAccount("M00006");
        var before = await Ledger(account);
        var w = await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/withdrawals", new WithdrawalRequestDto(1_000m, PayoutChannel.Cash, null, "Cash at counter"))).ReadAs<WithdrawalResponse>();
        w.Status.ShouldBe(WithdrawalStatus.Paid);
        w.Fee.ShouldBe(50m);
        (await Ledger(account)).Balance.ShouldBe(before.Balance - 1_050m);
    }

    [Fact]
    public async Task Fosa_withdrawal_above_teller_limit_needs_a_different_approver_and_holds_funds_meanwhile()
    {
        var account = LedgerSeeder.FosaAccount("M00013");
        // Top up so the member can afford it.
        await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/deposits", new DepositRequest(70_000m, DepositChannel.Cash, $"RCPT-{Guid.NewGuid():N}"[..20], "top up"));
        var before = await Ledger(account);

        var w = await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/withdrawals", new WithdrawalRequestDto(60_000m, PayoutChannel.Cash, null, "Large"))).ReadAs<WithdrawalResponse>();
        w.Status.ShouldBe(WithdrawalStatus.PendingApproval);

        var held = await Ledger(account);
        held.Balance.ShouldBe(before.Balance);
        held.HeldAmount.ShouldBe(before.HeldAmount + 60_050m);

        (await Teller.PostAsync($"/api/savings/withdrawals/{w.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // no permission
        var selfApprove = _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Savings.WithdrawalApprove);
        var self = await selfApprove.PostAsync($"/api/savings/withdrawals/{w.Id}/approve", null);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await self.Content.ReadAsStringAsync()).ShouldContain("maker_checker.same_user");

        (await (await Manager.PostAsync($"/api/savings/withdrawals/{w.Id}/approve", null)).ReadAs<WithdrawalResponse>()).Status.ShouldBe(WithdrawalStatus.Approved);
        var paid = await (await Teller.PostAsync($"/api/savings/withdrawals/{w.Id}/pay", null)).ReadAs<WithdrawalResponse>();
        paid.Status.ShouldBe(WithdrawalStatus.Paid);
        var after = await Ledger(account);
        after.Balance.ShouldBe(before.Balance - 60_050m);
        after.HeldAmount.ShouldBe(before.HeldAmount);
    }

    [Fact]
    public async Task Bosa_withdrawal_requires_notice_period_before_payout()
    {
        var account = LedgerSeeder.SavingsAccount("M00007");
        // Seeded deposits may be pledged/guaranteed; make sure there is free balance for the notice withdrawal.
        await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/deposits", new DepositRequest(5_000m, DepositChannel.CheckOff, $"RCPT-{Guid.NewGuid():N}"[..20], "check-off"));
        var heldBefore = (await Ledger(account)).HeldAmount;
        var w = await (await Teller.PostAsJsonAsync($"/api/savings/accounts/{account}/withdrawals", new WithdrawalRequestDto(2_000m, PayoutChannel.Cash, null, "Notice"))).ReadAs<WithdrawalResponse>();
        w.Status.ShouldBe(WithdrawalStatus.PendingApproval);
        w.NoticeExpiresOn.ShouldBeGreaterThan(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(55)));
        await (await Manager.PostAsync($"/api/savings/withdrawals/{w.Id}/approve", null)).ReadAs<WithdrawalResponse>();
        var pay = await Teller.PostAsync($"/api/savings/withdrawals/{w.Id}/pay", null);
        pay.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await pay.Content.ReadAsStringAsync()).ShouldContain("savings.withdrawal.notice_not_expired");
        // Reject releases the hold.
        var rejected = await (await Manager.PostAsJsonAsync($"/api/savings/withdrawals/{w.Id}/reject", new ReasonDto("Member withdrew the notice"))).ReadAs<WithdrawalResponse>();
        rejected.Status.ShouldBe(WithdrawalStatus.Rejected);
        (await Ledger(account)).HeldAmount.ShouldBe(heldBefore);
    }

    [Fact]
    public async Task Fixed_deposit_moves_funds_fosa_to_bosa_and_pays_interest_at_maturity()
    {
        var memberId = DemoTenant.MemberId("M00012");
        var fosa = LedgerSeeder.FosaAccount("M00012");
        await Teller.PostAsJsonAsync($"/api/savings/accounts/{fosa}/deposits", new DepositRequest(30_000m, DepositChannel.Cash, $"RCPT-{Guid.NewGuid():N}"[..20], "for FD"));
        var fosaBefore = (await Ledger(fosa)).Balance;

        var fd = await (await Teller.PostAsJsonAsync("/api/savings/fixed-deposits", new OpenFixedDepositRequest(memberId, SavingsSeeder.FixedDeposit6, 20_000m, fosa))).ReadAs<SavingsAccountResponse>();
        fd.Kind.ShouldBe(ProductKind.FixedDeposit);
        fd.AccountNumber.ShouldStartWith("M00012-FD");
        (await Ledger(fosa)).Balance.ShouldBe(fosaBefore - 20_000m);
        var fdLedger = await Ledger(fd.AccountNumber);
        fdLedger.Balance.ShouldBe(20_000m);
        fdLedger.Segment.ShouldBe(Segment.Bosa);

        (await Teller.PostAsync($"/api/savings/fixed-deposits/{fd.AccountNumber}/mature", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var matured = await (await Teller.PostAsync($"/api/savings/fixed-deposits/{fd.AccountNumber}/mature?force=true", null)).ReadAs<SavingsAccountResponse>();
        matured.Status.ShouldBe(SavingsAccountStatus.Closed);
        (await Ledger(fosa)).Balance.ShouldBe(fosaBefore - 20_000m + 20_000m + 800m); // 8% p.a. × 6 months
        (await Ledger(fd.AccountNumber)).Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task Dividend_declaration_is_maker_checker_and_posts_appropriation_then_pays_net_of_wht()
    {
        // Basis is the average daily balance over the year: FY2024 predates the seeded history (nothing to pay) and the
        // current year has not ended. The seed declares FY2025; the checker steps run on that declaration.
        var tooEarly = await Accountant.PostAsJsonAsync("/api/savings/dividends", new DeclareDividendRequest(2024, 500, 400));
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await tooEarly.Content.ReadAsStringAsync()).ShouldContain("savings.dividend.nothing_to_pay");
        var open = await Accountant.PostAsJsonAsync("/api/savings/dividends", new DeclareDividendRequest(DateTime.UtcNow.Year, 500, 400));
        open.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await open.Content.ReadAsStringAsync()).ShouldContain("savings.dividend.year_open");
        var declared = (await (await Accountant.GetAsync("/api/savings/dividends")).ReadAs<List<DividendResponse>>()).Single(d => d.FinancialYear == 2025 && d.Status == DividendStatus.Declared);
        declared.Lines.ShouldNotBeEmpty();
        declared.TotalWithholdingTax.ShouldBeGreaterThan(0);

        var self = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Savings.DividendsApprove);
        (await self.PostAsync($"/api/savings/dividends/{declared.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var payableBefore = await Gl("2400");
        var approved = await (await Manager.PostAsync($"/api/savings/dividends/{declared.Id}/approve", null)).ReadAs<DividendResponse>();
        approved.Status.ShouldBe(DividendStatus.Approved);
        (await Gl("2400") - payableBefore).ShouldBe(declared.TotalShareDividend);

        var line = declared.Lines.First(l => l.PayoutAccountNumber != null);
        var fosaBefore = (await Ledger(line.PayoutAccountNumber!)).Balance;
        var paid = await (await Manager.PostAsync($"/api/savings/dividends/{declared.Id}/pay", null)).ReadAs<DividendResponse>();
        paid.Status.ShouldBe(DividendStatus.Paid);
        (await Ledger(line.PayoutAccountNumber!)).Balance.ShouldBe(fosaBefore + line.NetPayable);
        (await Gl("2400")).ShouldBe(payableBefore);

        var recon = await (await Teller.GetAsync("/api/ledger/reconciliation")).ReadAs<ReconciliationReport>();
        recon.IsClean.ShouldBeTrue();
        var tb = await (await Teller.GetAsync("/api/ledger/trial-balance?segment=Bosa")).ReadAs<TrialBalance>();
        tb.IsBalanced.ShouldBeTrue();
    }

    [Fact]
    public async Task Member_savings_summary_feeds_loan_eligibility()
    {
        using var scope = _factory.TenantScope();
        var savings = (ISavingsService)scope.ServiceProvider.GetService(typeof(ISavingsService))!;
        var summary = await savings.GetMemberSummaryAsync(DemoTenant.MemberId("M00001"), CancellationToken.None);
        summary.BosaDeposits.ShouldBeGreaterThan(0);
        summary.Shares.ShouldBeGreaterThanOrEqualTo(10_000m);
        summary.MonthsWithContributions.ShouldBeGreaterThanOrEqualTo(6);
        summary.FosaAccountNumber.ShouldBe(LedgerSeeder.FosaAccount("M00001"));
    }
}
