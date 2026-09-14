using Sacco.Modules.Lending.Domain;
using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Shouldly;

namespace Sacco.UnitTests.Lending;

public class LoanDomainTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Officer = Guid.NewGuid();
    private static readonly Guid Committee1 = Guid.NewGuid();
    private static readonly Guid Committee2 = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 11);

    private static LoanProduct Product(bool guarantors = true, decimal threshold = 100_000m, int approvals = 2) =>
        LoanProduct.Create(Guid.NewGuid(), Tenant, "DEV", "Dev", null, Segment.Bosa, "1100", "4000", "1200", "4100", "1900", "5100", 1200, InterestMethod.ReducingBalance, 10_000, 3_000_000, 6, 48, 3m, 6, 100, guarantors, 2, threshold, approvals, 5);

    private static EligibilitySnapshot Eligible(decimal deposits = 50_000m) => new() { BosaDeposits = deposits, DepositMultiplier = 3, MaxEligibleAmount = deposits * 3, MembershipMonths = 12, ContributionMonths = 12 };

    private static Loan NewLoan(decimal amount = 90_000m, LoanProduct? product = null) =>
        Loan.Apply(Guid.NewGuid(), Tenant, "LN-000001", Guid.NewGuid(), product ?? Product(), amount, 12, "School fees", "M00001-FO", Eligible(), Officer, Now);

    [Fact]
    public void Reducing_balance_schedule_sums_to_principal_and_declines_in_interest()
    {
        var s = Loan.BuildSchedule(Guid.NewGuid(), 100_000m, 1200, 12, InterestMethod.ReducingBalance, Today);
        s.Count.ShouldBe(12);
        s.Sum(i => i.PrincipalDue).ShouldBe(100_000m);
        s[0].InterestDue.ShouldBe(1_000m); // 1% per month on 100,000
        s[11].InterestDue.ShouldBeLessThan(s[0].InterestDue);
        s.Select(i => i.PrincipalDue + i.InterestDue).Take(11).Distinct().Count().ShouldBe(1, "annuity instalments are equal except the last");
        s[0].DueDate.ShouldBe(Today.AddMonths(1));
    }

    [Fact]
    public void Flat_schedule_spreads_total_interest_evenly()
    {
        var s = Loan.BuildSchedule(Guid.NewGuid(), 15_000m, 500, 3, InterestMethod.Flat, Today);
        s.Sum(i => i.InterestDue).ShouldBe(187.50m); // 15,000 × 5% × 3/12
        s.Sum(i => i.PrincipalDue).ShouldBe(15_000m);
    }

    [Fact]
    public void Eligibility_limits_are_enforced_at_application()
    {
        Should.Throw<DomainRuleException>(() => Loan.Apply(Guid.NewGuid(), Tenant, "LN-1", Guid.NewGuid(), Product(), 200_000m, 12, "x", "M-FO", Eligible(50_000m), Officer, Now)).Code.ShouldBe("loans.exceeds_eligibility");
        var young = Eligible(); young.MembershipMonths = 2;
        Should.Throw<DomainRuleException>(() => Loan.Apply(Guid.NewGuid(), Tenant, "LN-1", Guid.NewGuid(), Product(), 50_000m, 12, "x", "M-FO", young, Officer, Now)).Code.ShouldBe("loans.membership_too_short");
        Should.Throw<DomainRuleException>(() => Loan.Apply(Guid.NewGuid(), Tenant, "LN-1", Guid.NewGuid(), Product(), 50_000m, 60, "x", "M-FO", Eligible(), Officer, Now)).Code.ShouldBe("loans.term_out_of_range");
    }

    [Fact]
    public void Approval_is_n_of_m_with_segregation_of_duties()
    {
        var product = Product();
        var loan = NewLoan(150_000m, product); // above threshold → 2 approvals
        loan.AddGuarantor(Guid.NewGuid(), "G1-SV", 60_000m, Now).Accept(Now);
        loan.AddGuarantor(Guid.NewGuid(), "G2-SV", 50_000m, Now).Accept(Now);
        loan.Appraise(Officer, "ok", Now);

        Should.Throw<MakerCheckerViolationException>(() => loan.Approve(Officer, null, product, 50_000m, Now)); // originator/appraiser
        loan.Approve(Committee1, null, product, 50_000m, Now).ShouldBeFalse();
        loan.Status.ShouldBe(LoanStatus.PendingApproval);
        Should.Throw<DomainRuleException>(() => loan.Approve(Committee1, null, product, 50_000m, Now)).Code.ShouldBe("loans.already_approved_by_user");
        loan.Approve(Committee2, null, product, 50_000m, Now).ShouldBeTrue();
        loan.Status.ShouldBe(LoanStatus.Approved);

        Should.Throw<MakerCheckerViolationException>(() => loan.Disburse(Officer, "LN-000001", Today, Now));
        loan.Disburse(Manager, "LN-000001", Today, Now);
        loan.Status.ShouldBe(LoanStatus.Active);
        loan.Schedule.Count.ShouldBe(12);
    }

    [Fact]
    public void Small_loans_need_one_approval_but_guarantee_coverage_is_still_checked()
    {
        var product = Product();
        var loan = NewLoan(50_000m, product);
        loan.Appraise(Officer, "ok", Now);
        Should.Throw<DomainRuleException>(() => loan.Approve(Committee1, null, product, 0m, Now)).Code.ShouldBe("loans.guarantors_insufficient");
        loan.AddGuarantor(Guid.NewGuid(), "G1-SV", 10_000m, Now).Accept(Now);
        loan.AddGuarantor(Guid.NewGuid(), "G2-SV", 10_000m, Now).Accept(Now);
        Should.Throw<DomainRuleException>(() => loan.Approve(Committee1, null, product, 0m, Now)).Code.ShouldBe("loans.undersecured");
        loan.Approve(Committee1, null, product, 30_000m, Now).ShouldBeTrue(); // 20k guarantees + 30k own deposits
    }

    [Fact]
    public void Repayment_allocates_interest_then_principal_and_tracks_arrears()
    {
        var product = Product(guarantors: false);
        var loan = NewLoan(12_000m, product);
        loan.Appraise(Officer, "ok", Now);
        loan.Approve(Committee1, null, product, 0, Now);
        loan.Disburse(Manager, "LN-000001", Today.AddMonths(-3), Now);
        loan.DaysInArrears(Today, 5).ShouldBeGreaterThan(50); // 3 instalments overdue
        loan.ArrearsAmount(Today).ShouldBeGreaterThan(0);

        var first = loan.Schedule.First();
        var (interest, principal, _) = loan.AllocateRepayment(first.TotalDue, Now);
        interest.ShouldBe(first.InterestDue);
        principal.ShouldBe(first.PrincipalDue);
        first.Status.ShouldBe(InstallmentStatus.Paid);
        loan.IsFullyRepaid.ShouldBeFalse();

        loan.AllocateRepayment(loan.Schedule.Where(i => i.Status != InstallmentStatus.Paid).Sum(i => i.Outstanding), Now);
        loan.IsFullyRepaid.ShouldBeTrue();
    }

    [Fact]
    public void Provisioning_config_classifies_by_days_in_arrears()
    {
        var config = ProvisioningConfig.Create(Guid.NewGuid(), Tenant,
            [new("Performing", 0, 100), new("Watch", 31, 500), new("Substandard", 181, 2500), new("Doubtful", 361, 5000), new("Loss", 721, 10000)], "regs", Officer, Now);
        config.Classify(0).Name.ShouldBe("Performing");
        config.Classify(30).Name.ShouldBe("Performing");
        config.Classify(31).Name.ShouldBe("Watch");
        config.Classify(200).Name.ShouldBe("Substandard");
        config.Classify(400).Name.ShouldBe("Doubtful");
        config.Classify(900).ProvisionRateBps.ShouldBe(10000);
        Should.Throw<DomainRuleException>(() => ProvisioningConfig.Create(Guid.NewGuid(), Tenant, [new("Watch", 31, 500)], null, Officer, Now));
    }

    [Fact]
    public void Provisioning_run_is_maker_checker()
    {
        var run = ProvisioningRun.Create(Guid.NewGuid(), Tenant, Today, Officer, Now, [new(Guid.NewGuid(), "LN-1", Guid.NewGuid(), "DEV", Segment.Bosa, 200, "Substandard", 2500, 40_000m, 10_000m, 10_000m)]);
        run.TotalProvisionRequired.ShouldBe(10_000m);
        Should.Throw<MakerCheckerViolationException>(() => run.Approve(Officer, Now));
        run.Approve(Manager, Now);
        run.Status.ShouldBe(ProvisioningRunStatus.Posted);
    }
}
