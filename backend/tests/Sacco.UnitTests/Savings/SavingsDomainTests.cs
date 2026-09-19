using Sacco.Modules.Savings.Application;
using Sacco.Modules.Savings.Domain;
using Sacco.Shared.Domain;
using Sacco.Shared.Savings;
using Shouldly;

namespace Sacco.UnitTests.Savings;

public class SavingsDomainTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Teller = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 11);

    private static SavingsProduct Fosa(decimal tellerLimit = 50_000m) => SavingsProduct.Create(Guid.NewGuid(), Tenant, "FOSA-CUR", "FOSA Current", null, ProductKind.FosaCurrent, Segment.Fosa, "2200", "FO", 500, 200, true, 0, tellerLimit, 50, "4200", 0, null, null);
    private static SavingsProduct Bosa() => SavingsProduct.Create(Guid.NewGuid(), Tenant, "BOSA-DEP", "Deposits", null, ProductKind.BosaDeposit, Segment.Bosa, "2000", "SV", 1000, 0, true, 60, 0, 0, null, 0, null, null);
    private static SavingsProduct Fd() => SavingsProduct.Create(Guid.NewGuid(), Tenant, "FD-12M", "FD", null, ProductKind.FixedDeposit, Segment.Bosa, "2100", "FD", 20_000, 0, false, 0, 0, 0, null, 1000, "5000", 12);

    [Fact]
    public void Product_segment_is_fixed_by_kind()
    {
        Should.Throw<DomainRuleException>(() => SavingsProduct.Create(Guid.NewGuid(), Tenant, "X", "X", null, ProductKind.BosaDeposit, Segment.Fosa, "2000", "SV", 0, 0, true, 60, 0, 0, null, 0, null, null))
            .Code.ShouldBe("savings.product.segment_mismatch");
        Should.Throw<DomainRuleException>(() => SavingsProduct.Create(Guid.NewGuid(), Tenant, "SH", "Shares", null, ProductKind.Shares, Segment.Bosa, "3000", "SH", 0, 0, true, 0, 0, 0, null, 0, null, null))
            .Code.ShouldBe("savings.product.shares_not_withdrawable");
    }

    [Fact]
    public void Fixed_deposit_interest_is_simple_interest_for_the_term()
    {
        Fd().FixedDepositInterest(20_000m).ShouldBe(2_000m);
        var account = SavingsAccount.OpenFixedDeposit(Guid.NewGuid(), Tenant, "M00001-FD1", Guid.NewGuid(), Fd(), 50_000m, "M00001-FO", Teller, Now, Today);
        account.MaturityInterest().ShouldBe(5_000m);
        account.MaturityDate.ShouldBe(Today.AddMonths(12));
        Should.Throw<DomainRuleException>(() => account.Mature(Today.AddMonths(6))).Code.ShouldBe("savings.fd.not_yet_matured");
        account.Mature(Today.AddMonths(12));
        account.Status.ShouldBe(SavingsAccountStatus.Matured);
    }

    [Fact]
    public void Teller_payout_applies_only_to_on_demand_cash_within_limit()
    {
        var fosa = Fosa();
        var account = SavingsAccount.Open(Guid.NewGuid(), Tenant, "M00001-FO", Guid.NewGuid(), fosa, Teller, Now);
        WithdrawalRequest.Create(Guid.NewGuid(), Tenant, account, fosa, 20_000m, FeeCharge.ProductDefault(fosa), PayoutChannel.Cash, null, null, Teller, Now, Today).QualifiesForTellerPayout(fosa).ShouldBeTrue();
        WithdrawalRequest.Create(Guid.NewGuid(), Tenant, account, fosa, 60_000m, FeeCharge.ProductDefault(fosa), PayoutChannel.Cash, null, null, Teller, Now, Today).QualifiesForTellerPayout(fosa).ShouldBeFalse();
        WithdrawalRequest.Create(Guid.NewGuid(), Tenant, account, fosa, 1_000m, FeeCharge.ProductDefault(fosa), PayoutChannel.MPesa, "254700100001", null, Teller, Now, Today).QualifiesForTellerPayout(fosa).ShouldBeFalse();

        var bosa = Bosa();
        var deposits = SavingsAccount.Open(Guid.NewGuid(), Tenant, "M00001-SV", Guid.NewGuid(), bosa, Teller, Now);
        var notice = WithdrawalRequest.Create(Guid.NewGuid(), Tenant, deposits, bosa, 1_000m, FeeCharge.ProductDefault(bosa), PayoutChannel.Cash, null, null, Teller, Now, Today);
        notice.QualifiesForTellerPayout(bosa).ShouldBeFalse();
        notice.NoticeExpiresOn.ShouldBe(Today.AddDays(60));
    }

    [Fact]
    public void Withdrawal_is_maker_checker_and_respects_notice()
    {
        var bosa = Bosa();
        var deposits = SavingsAccount.Open(Guid.NewGuid(), Tenant, "M00001-SV", Guid.NewGuid(), bosa, Teller, Now);
        var w = WithdrawalRequest.Create(Guid.NewGuid(), Tenant, deposits, bosa, 1_000m, FeeCharge.ProductDefault(bosa), PayoutChannel.Cash, null, null, Teller, Now, Today);
        Should.Throw<MakerCheckerViolationException>(() => w.Approve(Teller, Now));
        w.Approve(Manager, Now);
        Should.Throw<DomainRuleException>(() => w.MarkPaid(Teller, "REF", Now, Today.AddDays(59))).Code.ShouldBe("savings.withdrawal.notice_not_expired");
        w.MarkPaid(Teller, "REF", Now, Today.AddDays(60));
        w.Status.ShouldBe(WithdrawalStatus.Paid);
    }

    [Fact]
    public void Non_cash_withdrawals_need_a_destination_and_shares_cannot_be_withdrawn()
    {
        var fosa = Fosa();
        var account = SavingsAccount.Open(Guid.NewGuid(), Tenant, "M00001-FO", Guid.NewGuid(), fosa, Teller, Now);
        Should.Throw<DomainRuleException>(() => WithdrawalRequest.Create(Guid.NewGuid(), Tenant, account, fosa, 100m, FeeCharge.ProductDefault(fosa), PayoutChannel.MPesa, null, null, Teller, Now, Today)).Code.ShouldBe("savings.withdrawal.destination_required");
        var shares = SavingsProduct.Create(Guid.NewGuid(), Tenant, "SHARES", "Shares", null, ProductKind.Shares, Segment.Bosa, "3000", "SH", 10_000, 10_000, false, 0, 0, 0, null, 0, null, null);
        var sh = SavingsAccount.Open(Guid.NewGuid(), Tenant, "M00001-SH", Guid.NewGuid(), shares, Teller, Now);
        Should.Throw<DomainRuleException>(() => WithdrawalRequest.Create(Guid.NewGuid(), Tenant, sh, shares, 100m, FeeCharge.ProductDefault(shares), PayoutChannel.Cash, null, null, Teller, Now, Today)).Code.ShouldBe("savings.withdrawal.not_allowed");
    }

    [Fact]
    public void Dividend_lines_apply_rates_and_withholding_tax_and_require_a_different_approver()
    {
        var member = Guid.NewGuid();
        var d = DividendDeclaration.Declare(Guid.NewGuid(), Tenant, 2025, 800, 600, 500, Teller, Now,
        [
            new DividendLineDraft(member, "M1-SH", 10_000m, "M1-SV", 50_000m, "M1-FO"),
            new DividendLineDraft(Guid.NewGuid(), "M2-SH", 0m, "M2-SV", 0m, null),
        ]);
        d.Lines.Count.ShouldBe(1);
        var line = d.Lines[0];
        line.ShareDividend.ShouldBe(800m);
        line.DepositInterest.ShouldBe(3_000m);
        line.WithholdingTax.ShouldBe(190m);
        line.NetPayable.ShouldBe(3_610m);
        d.TotalShareDividend.ShouldBe(800m);
        Should.Throw<MakerCheckerViolationException>(() => d.Approve(Teller, Now));
        d.Approve(Manager, Now);
        d.Status.ShouldBe(DividendStatus.Approved);
    }

    [Fact]
    public void Posting_builder_bridges_segments_so_each_segment_balances()
    {
        var s = new SavingsSettings();
        var lines = PostingBuilder.Inflow(s, s.TellerCashGl, Segment.Fosa, "2000", "M00001-SV", Segment.Bosa, 1_000m, "deposit");
        lines.Count.ShouldBe(4);
        foreach (var seg in new[] { Segment.Fosa, Segment.Bosa })
            lines.Where(l => l.Segment == seg && l.Direction == EntryDirection.Debit).Sum(l => l.Amount)
                .ShouldBe(lines.Where(l => l.Segment == seg && l.Direction == EntryDirection.Credit).Sum(l => l.Amount));

        var sameSegment = PostingBuilder.Outflow(s, s.TellerCashGl, Segment.Fosa, "2200", "M00001-FO", Segment.Fosa, 1_000m, 50m, "4200", Segment.Fosa, "withdrawal");
        sameSegment.Count.ShouldBe(3);
        sameSegment.Sum(l => l.Direction == EntryDirection.Debit ? l.Amount : -l.Amount).ShouldBe(0m);
    }
}
