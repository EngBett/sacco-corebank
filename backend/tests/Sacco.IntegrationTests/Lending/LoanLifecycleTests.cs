using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Application;
using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Shouldly;

namespace Sacco.IntegrationTests.Lending;

/// <summary>Phase 4 exit criterion: originate, guarantee, approve, disburse, see GL postings and provisioning classification.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoanLifecycleTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private HttpClient Officer => _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.View, Permissions.Loans.Originate, Permissions.Loans.Appraise, Permissions.Ledger.View);
    private HttpClient Committee1 => _factory.ClientAs(DemoTenant.Users.CreditCommittee1, Permissions.Loans.View, Permissions.Loans.Approve);
    private HttpClient Committee2 => _factory.ClientAs(DemoTenant.Users.CreditCommittee2, Permissions.Loans.View, Permissions.Loans.Approve);
    private HttpClient Manager => _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Loans.View, Permissions.Loans.Approve, Permissions.Loans.Disburse, Permissions.Ledger.JournalApprove, Permissions.Ledger.View);
    private HttpClient Teller => _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Loans.View, Permissions.Loans.Repay, Permissions.Ledger.View);
    private HttpClient Accountant => _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Loans.View, Permissions.Loans.ProvisioningManage, Permissions.Ledger.View);

    private async Task<LedgerAccountSnapshot> Ledger(string number) => await (await Officer.GetAsync($"/api/ledger/accounts/{number}")).ReadAs<LedgerAccountSnapshot>();
    private async Task<decimal> Gl(string code) => (await (await Officer.GetAsync($"/api/ledger/gl-accounts/{code}")).ReadAs<Sacco.Modules.Ledger.Endpoints.GlAccountResponse>()).Balance;

    [Fact]
    public async Task Full_lifecycle_with_two_committee_approvals_and_correct_gl_postings()
    {
        var borrower = DemoTenant.MemberId("M00015");
        var fosa = LedgerSeeder.FosaAccount("M00015");
        var eligibility = await (await Officer.GetAsync($"/api/loans/eligibility?memberId={borrower}&productCode={LendingSeeder.Development}")).ReadAs<EligibilityResponse>();
        eligibility.MaxEligibleAmount.ShouldBeGreaterThan(0);

        var tooMuch = await Officer.PostAsJsonAsync("/api/loans", new ApplyLoanRequest(borrower, LendingSeeder.Development, eligibility.MaxEligibleAmount + 1_000m, 12, "Too much", null));
        tooMuch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var amount = Math.Min(eligibility.MaxEligibleAmount, 110_000m); // above the 100k committee threshold if eligible
        amount.ShouldBeGreaterThan(100_000m, "M00015 has enough deposits for a committee-sized loan in the seed");
        var loan = await (await Officer.PostAsJsonAsync("/api/loans", new ApplyLoanRequest(borrower, LendingSeeder.Development, amount, 24, "Business expansion", null))).ReadAs<LoanResponse>();
        loan.Status.ShouldBe(LoanStatus.Applied);
        loan.ApprovalsRequired.ShouldBe(2);
        loan.ProcessingFee.ShouldBe(decimal.Round(amount * 0.01m, 2));

        // Guarantors: self-guarantee is rejected; two accepted guarantees place ledger holds.
        (await Officer.PostAsJsonAsync($"/api/loans/{loan.Id}/guarantors", new AddGuarantorRequest(borrower, 10_000m))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var depositor = _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Savings.Deposit);
        foreach (var (g, amt) in new[] { ("M00016", 40_000m), ("M00011", 30_000m) })
        {
            // Seeded guarantors are already committed elsewhere; give them fresh deposits to guarantee with.
            (await depositor.PostAsJsonAsync($"/api/savings/accounts/{LedgerSeeder.SavingsAccount(g)}/deposits", new Sacco.Modules.Savings.Endpoints.DepositRequest(amt + 5_000m, Sacco.Shared.Savings.DepositChannel.CheckOff, $"RCPT-{Guid.NewGuid():N}"[..20], "check-off"))).EnsureSuccessStatusCode();
            var gs = await (await Officer.PostAsJsonAsync($"/api/loans/{loan.Id}/guarantors", new AddGuarantorRequest(DemoTenant.MemberId(g), amt))).ReadAs<List<GuarantorResponse>>();
            var gid = gs.Single(x => x.GuarantorMemberId == DemoTenant.MemberId(g)).Id;
            var before = (await Ledger(LedgerSeeder.SavingsAccount(g))).HeldAmount;
            (await Officer.PostAsync($"/api/loans/{loan.Id}/guarantors/{gid}/accept", null)).EnsureSuccessStatusCode();
            (await Ledger(LedgerSeeder.SavingsAccount(g))).HeldAmount.ShouldBe(before + amt);
        }

        (await Officer.PostAsJsonAsync($"/api/loans/{loan.Id}/appraise", new NotesRequest("Payslips verified"))).EnsureSuccessStatusCode();

        // Originator cannot approve; committee 1 alone is not enough; committee 2 completes.
        var self = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.Approve);
        (await self.PostAsJsonAsync($"/api/loans/{loan.Id}/approve", new NotesRequest(null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var one = await (await Committee1.PostAsJsonAsync($"/api/loans/{loan.Id}/approve", new NotesRequest("ok"))).ReadAs<LoanResponse>();
        one.Status.ShouldBe(LoanStatus.PendingApproval);
        var two = await (await Committee2.PostAsJsonAsync($"/api/loans/{loan.Id}/approve", new NotesRequest("ok"))).ReadAs<LoanResponse>();
        two.Status.ShouldBe(LoanStatus.Approved);
        two.PledgedDepositsAmount.ShouldBeGreaterThan(0);

        // Disbursement: loan control up, FOSA account credited net of fee, fee income up, clearing balanced.
        var fosaBefore = (await Ledger(fosa)).Balance;
        var controlBefore = await Gl("1100");
        var feeBefore = await Gl("4100");
        var active = await (await Manager.PostAsync($"/api/loans/{loan.Id}/disburse", null)).ReadAs<LoanResponse>();
        active.Status.ShouldBe(LoanStatus.Active);
        active.Schedule.Count.ShouldBe(24);
        active.OutstandingPrincipal.ShouldBe(amount);
        ((await Ledger(fosa)).Balance - fosaBefore).ShouldBe(amount - loan.ProcessingFee);
        (await Gl("1100") - controlBefore).ShouldBe(amount);
        (await Gl("4100") - feeBefore).ShouldBe(loan.ProcessingFee);

        // Repayment of the first instalment from the FOSA account.
        var first = active.Schedule[0];
        var repaid = await (await Teller.PostAsJsonAsync($"/api/loans/{active.LoanNumber}/repayments", new RepayRequest(first.PrincipalDue + first.InterestDue, RepaymentChannel.FosaAccount, $"RCPT-{Guid.NewGuid():N}"[..20], null))).ReadAs<RepaymentResult>();
        repaid.PrincipalPaid.ShouldBe(first.PrincipalDue);
        repaid.InterestPaid.ShouldBe(first.InterestDue);
        repaid.OutstandingPrincipal.ShouldBe(amount - first.PrincipalDue);

        var recon = await (await Officer.GetAsync("/api/ledger/reconciliation")).ReadAs<ReconciliationReport>();
        recon.IsClean.ShouldBeTrue();
        var tb = await (await Officer.GetAsync("/api/ledger/trial-balance?segment=Bosa")).ReadAs<TrialBalance>();
        tb.IsBalanced.ShouldBeTrue();
    }

    [Fact]
    public async Task Seeded_portfolio_has_a_loan_in_every_aging_bucket_and_provisioning_is_maker_checker()
    {
        var aging = await (await Accountant.GetAsync("/api/loans/provisioning/aging")).ReadAs<AgingReport>();
        foreach (var bucket in new[] { "Performing", "Watch", "Substandard", "Doubtful", "Loss" })
            aging.Buckets.Single(b => b.Bucket == bucket).Loans.ShouldBeGreaterThan(0, $"expected a seeded loan in bucket {bucket}");
        aging.TotalProvisionRequired.ShouldBeGreaterThan(0);
        aging.NonPerformingOutstanding.ShouldBeGreaterThan(0);
        aging.Loans.Single(l => l.Bucket == "Loss").ProvisionRateBps.ShouldBe(10000);

        var runs = await (await Accountant.GetAsync("/api/loans/provisioning/runs")).ReadAs<List<ProvisioningRunResponse>>();
        var pending = runs.Single(r => r.Status == ProvisioningRunStatus.PendingApproval);
        var selfApprove = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.JournalApprove);
        (await selfApprove.PostAsync($"/api/loans/provisioning/runs/{pending.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var provisionBefore = await Gl("1900");
        var posted = await (await Manager.PostAsync($"/api/loans/provisioning/runs/{pending.Id}/approve", null)).ReadAs<ProvisioningRunResponse>();
        posted.Status.ShouldBe(ProvisioningRunStatus.Posted);
        var bosaRequired = pending.Lines.Where(l => l.Segment == Segment.Bosa).Sum(l => l.ProvisionRequired);
        (await Gl("1900")).ShouldBe(bosaRequired);
        provisionBefore.ShouldNotBe(bosaRequired);
    }

    [Fact]
    public async Task Guarantor_exposure_cap_is_enforced()
    {
        var heavy = DemoTenant.MemberId("M00003");
        var exposure = await (await Officer.GetAsync($"/api/loans/guarantors/{heavy}/exposure")).ReadAs<GuarantorExposure>();
        exposure.ActiveGuarantees.ShouldBeGreaterThan(0);
        (exposure.Capacity - exposure.ActiveGuarantees).ShouldBeLessThan(exposure.Capacity * 0.5m, "the seed puts M00003 well into her cap");

        var borrower = DemoTenant.MemberId("M00012");
        var loan = await (await Officer.PostAsJsonAsync("/api/loans", new ApplyLoanRequest(borrower, LendingSeeder.Emergency, 20_000m, 6, "Test", null))).ReadAs<LoanResponse>();
        var r = await Officer.PostAsJsonAsync($"/api/loans/{loan.Id}/guarantors", new AddGuarantorRequest(heavy, exposure.Capacity)); // more than remaining room
        r.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync()).ShouldContain("loans.guarantor.exposure_cap");
    }

    [Fact]
    public async Task Seeded_pending_loans_exist_for_the_demo()
    {
        var pending = await (await Officer.GetAsync($"/api/loans?status={LoanStatus.PendingApproval}")).ReadAs<Sacco.Shared.Http.PagedResult<LoanListItem>>();
        pending.Items.ShouldContain(l => l.Amount == 150_000m);
        var applied = await (await Officer.GetAsync($"/api/loans?status={LoanStatus.Applied}")).ReadAs<Sacco.Shared.Http.PagedResult<LoanListItem>>();
        applied.Items.ShouldNotBeEmpty();
    }
}
