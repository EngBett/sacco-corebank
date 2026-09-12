using Sacco.Modules.Ledger.Domain;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Shouldly;

namespace Sacco.UnitTests.Ledger;

public class AccountTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Theory]
    [InlineData(GlAccountCategory.Asset, EntryDirection.Debit)]
    [InlineData(GlAccountCategory.Expense, EntryDirection.Debit)]
    [InlineData(GlAccountCategory.Liability, EntryDirection.Credit)]
    [InlineData(GlAccountCategory.Equity, EntryDirection.Credit)]
    [InlineData(GlAccountCategory.Income, EntryDirection.Credit)]
    public void Normal_balance_defaults_from_category(GlAccountCategory category, EntryDirection expected)
    {
        var a = GlAccount.Create(Guid.NewGuid(), Tenant, "1000", "Cash", category, Segment.Fosa, true, false, null, null);
        a.NormalBalance.ShouldBe(expected);
    }

    [Fact]
    public void Contra_account_can_override_normal_balance()
    {
        var a = GlAccount.Create(Guid.NewGuid(), Tenant, "1900", "Loan loss provision", GlAccountCategory.Asset, Segment.Bosa, true, false, null, null, EntryDirection.Credit);
        a.NormalBalance.ShouldBe(EntryDirection.Credit);
        a.DeltaFor(EntryDirection.Credit, 100m).ShouldBe(100m);
        a.DeltaFor(EntryDirection.Debit, 100m).ShouldBe(-100m);
    }

    [Fact]
    public void Delta_is_signed_toward_normal_side()
    {
        var cash = GlAccount.Create(Guid.NewGuid(), Tenant, "1010", "Cash", GlAccountCategory.Asset, Segment.Fosa, true, false, null, null);
        cash.DeltaFor(EntryDirection.Debit, 50m).ShouldBe(50m);
        cash.DeltaFor(EntryDirection.Credit, 50m).ShouldBe(-50m);
    }

    [Fact]
    public void Control_account_must_be_postable()
    {
        Should.Throw<DomainRuleException>(() => GlAccount.Create(Guid.NewGuid(), Tenant, "2000", "Deposits", GlAccountCategory.Liability, Segment.Bosa, false, true, null, null));
    }

    [Fact]
    public void Sub_ledger_account_inherits_segment_from_control_and_requires_control_account()
    {
        var control = GlAccount.Create(Guid.NewGuid(), Tenant, "2000", "Deposits", GlAccountCategory.Liability, Segment.Bosa, true, true, null, null);
        var a = LedgerAccount.Open(Guid.NewGuid(), Tenant, "M00001-SV", Guid.NewGuid(), control, LedgerAccountKind.Savings, "BOSA-DEP", Guid.NewGuid(), DateTimeOffset.UtcNow);
        a.Segment.ShouldBe(Segment.Bosa);
        a.Status.ShouldBe(LedgerAccountStatus.Active);

        var notControl = GlAccount.Create(Guid.NewGuid(), Tenant, "1000", "Cash", GlAccountCategory.Asset, Segment.Bosa, true, false, null, null);
        Should.Throw<DomainRuleException>(() => LedgerAccount.Open(Guid.NewGuid(), Tenant, "X", Guid.NewGuid(), notControl, LedgerAccountKind.Savings, "P", Guid.NewGuid(), DateTimeOffset.UtcNow))
            .Code.ShouldBe("ledger.account.not_control");
    }

    [Fact]
    public void Closed_account_cannot_change_status_again()
    {
        var control = GlAccount.Create(Guid.NewGuid(), Tenant, "2000", "Deposits", GlAccountCategory.Liability, Segment.Bosa, true, true, null, null);
        var a = LedgerAccount.Open(Guid.NewGuid(), Tenant, "M00001-SV", Guid.NewGuid(), control, LedgerAccountKind.Savings, "BOSA-DEP", Guid.NewGuid(), DateTimeOffset.UtcNow);
        a.SetStatus(LedgerAccountStatus.Closed, DateTimeOffset.UtcNow);
        Should.Throw<DomainRuleException>(() => a.SetStatus(LedgerAccountStatus.Active, DateTimeOffset.UtcNow));
    }
}
