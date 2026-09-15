using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Domain;
using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Shouldly;

namespace Sacco.UnitTests.Lending;

public sealed class LoanAdjustmentTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Officer = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 14);

    private static LoanProduct Product() =>
        LoanProduct.Create(Guid.NewGuid(), Tenant, "DEV", "Dev", null, Segment.Bosa, "1100", "4000", "1200", "4100", "1900", "5100", 1200, InterestMethod.ReducingBalance, 10_000, 3_000_000, 6, 48, 3m, 6, 100, false, 0, 100_000m, 1, 5);

    private static Loan ActiveLoan(decimal amount = 120_000m, int term = 12)
    {
        var product = Product();
        var loan = Loan.Apply(Guid.NewGuid(), Tenant, "LN-000001", Guid.NewGuid(), product, amount, term, "Plot", "M00001-FO", new EligibilitySnapshot { BosaDeposits = 100_000m, DepositMultiplier = 3, MaxEligibleAmount = 300_000m, MembershipMonths = 24, ContributionMonths = 24 }, Officer, Now);
        loan.Appraise(Guid.NewGuid(), "ok", Now);
        loan.Approve(Manager, null, product, 100_000m, Now);
        loan.Disburse(Guid.NewGuid(), "LN-000001", Today.AddMonths(-6), Now);
        return loan;
    }

    [Fact]
    public void Write_off_and_restructure_are_maker_checker()
    {
        var loan = ActiveLoan();
        var req = LoanAdjustment.RequestWriteOff(Tenant, loan, "Borrower deceased, estate insolvent", Officer, Now);
        req.Status.ShouldBe(LoanAdjustmentStatus.PendingApproval);
        Should.Throw<MakerCheckerViolationException>(() => req.Approve(Officer, null, Now));
        req.Approve(Manager, "Board resolution 12/2026", Now);
        req.Status.ShouldBe(LoanAdjustmentStatus.Approved);
        req.DecidedByUserId.ShouldBe(Manager);

        var restructure = LoanAdjustment.RequestRestructure(Tenant, loan, Product(), 24, 1000, "Salary cut", Officer, Now);
        Should.Throw<DomainRuleException>(() => restructure.Reject(Manager, "", Now)).Code.ShouldBe("loans.reason_required");
        restructure.Reject(Manager, "Not justified", Now);
        restructure.Status.ShouldBe(LoanAdjustmentStatus.Rejected);
    }

    [Fact]
    public void Requests_are_only_valid_on_active_loans_within_product_limits()
    {
        var product = Product();
        var applied = Loan.Apply(Guid.NewGuid(), Tenant, "LN-000002", Guid.NewGuid(), product, 50_000m, 12, "x", "M00001-FO", new EligibilitySnapshot { BosaDeposits = 100_000m, DepositMultiplier = 3, MaxEligibleAmount = 300_000m, MembershipMonths = 24, ContributionMonths = 24 }, Officer, Now);
        Should.Throw<DomainRuleException>(() => LoanAdjustment.RequestWriteOff(Tenant, applied, "x", Officer, Now)).Code.ShouldBe("loans.not_active");
        Should.Throw<DomainRuleException>(() => LoanAdjustment.RequestRestructure(Tenant, ActiveLoan(), product, 60, null, "too long", Officer, Now)).Code.ShouldBe("loans.restructure.term");
    }

    [Fact]
    public void Write_off_ends_the_contract_and_releases_guarantees()
    {
        var loan = ActiveLoan();
        loan.PledgeDeposits("M00001-SV", 50_000m);
        loan.WriteOff(Now);
        loan.Status.ShouldBe(LoanStatus.WrittenOff);
        loan.ClosedAt.ShouldBe(Now);
        loan.PledgedDepositsAmount.ShouldBe(0m);
        Should.Throw<DomainRuleException>(() => loan.AllocateRepayment(100m, Now)).Code.ShouldBe("loans.not_active");
    }

    [Fact]
    public void Restructure_rebuilds_the_schedule_over_the_outstanding_principal_and_carries_due_interest()
    {
        var loan = ActiveLoan(120_000m, 12);
        var oldInstalments = loan.Schedule.Count;
        var fresh = loan.Restructure(90_000m, 18, 1000, 2_500m, Today, Now);
        fresh.Count.ShouldBe(18);
        loan.Schedule.Count.ShouldBe(18);
        loan.TermMonths.ShouldBe(18);
        loan.InterestRateBps.ShouldBe(1000);
        loan.RestructureCount.ShouldBe(1);
        loan.Schedule.Sum(i => i.PrincipalDue).ShouldBe(90_000m);
        loan.Schedule.OrderBy(i => i.Number).First().InterestDue.ShouldBeGreaterThan(2_500m, "carried interest sits in the first instalment");
        loan.Schedule.OrderBy(i => i.Number).First().DueDate.ShouldBe(Today.AddMonths(1));
        oldInstalments.ShouldBe(12);
    }

    [Fact]
    public void Payoff_settles_everything_due_and_waives_future_interest()
    {
        var loan = ActiveLoan(120_000m, 12); // disbursed 6 months ago: 6 instalments due, 6 in the future
        var (interest, principal, _) = loan.Payoff(Today, Now);
        principal.ShouldBe(120_000m);
        interest.ShouldBe(loan.Schedule.Where(i => i.DueDate <= Today).Sum(i => i.InterestDue));
        loan.IsFullyRepaid.ShouldBeTrue();
        loan.Schedule.Where(i => i.DueDate > Today).ShouldAllBe(i => i.InterestDue == i.InterestPaid);
        loan.Schedule.ShouldAllBe(i => i.PaidAt == Now);
    }

    [Fact]
    public void Instalments_remember_when_they_were_paid_and_how_late()
    {
        var loan = ActiveLoan(120_000m, 12);
        var first = loan.Schedule.OrderBy(i => i.Number).First();
        var paidOn = new DateTimeOffset(first.DueDate.AddDays(40).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        loan.AllocateRepayment(first.TotalDue, paidOn);
        first.PaidAt.ShouldBe(paidOn);
        first.DaysLate(graceDays: 5).ShouldBe(35);
        loan.Schedule.OrderBy(i => i.Number).Skip(1).First().DaysLate(5).ShouldBe(0, "unpaid instalments are not late until paid");
    }

    [Fact]
    public void Maintenance_next_run_is_always_in_the_future()
    {
        var at = new TimeOnly(2, 0);
        LendingMaintenanceService.NextRunDelay(new DateTimeOffset(2026, 9, 14, 1, 30, 0, TimeSpan.Zero), at).ShouldBe(TimeSpan.FromMinutes(30));
        LendingMaintenanceService.NextRunDelay(new DateTimeOffset(2026, 9, 14, 2, 0, 0, TimeSpan.Zero), at).ShouldBe(TimeSpan.FromHours(24));
        LendingMaintenanceService.NextRunDelay(new DateTimeOffset(2026, 9, 14, 23, 0, 0, TimeSpan.Zero), at).ShouldBe(TimeSpan.FromHours(3));
    }
}
