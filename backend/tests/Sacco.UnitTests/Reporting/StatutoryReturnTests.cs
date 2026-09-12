using Sacco.Modules.Reporting.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Reporting;

public class StatutoryReturnTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Generator = Guid.NewGuid();
    private static readonly Guid Submitter = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Submission_requires_reconciliation_a_reference_and_a_different_user()
    {
        var unreconciled = StatutoryReturn.Create(Guid.NewGuid(), Tenant, new(2026, 4, 1), new(2026, 6, 30), Generator, Now, false, "Trial balance FAILED", "{}");
        Should.Throw<DomainRuleException>(() => unreconciled.Submit(Submitter, "SASRA-123", Now)).Code.ShouldBe("reporting.not_reconciled");

        var ret = StatutoryReturn.Create(Guid.NewGuid(), Tenant, new(2026, 4, 1), new(2026, 6, 30), Generator, Now, true, "OK", "{}");
        Should.Throw<MakerCheckerViolationException>(() => ret.Submit(Generator, "SASRA-123", Now));
        Should.Throw<DomainRuleException>(() => ret.Submit(Submitter, " ", Now)).Code.ShouldBe("reporting.submission_reference_required");
        ret.Submit(Submitter, "SASRA-123", Now);
        ret.Status.ShouldBe(ReturnStatus.Submitted);
        Should.Throw<DomainRuleException>(() => ret.Withdraw(Generator, "oops")).Code.ShouldBe("reporting.not_generated");
    }

    [Fact]
    public void Period_must_be_ordered()
    {
        Should.Throw<DomainRuleException>(() => StatutoryReturn.Create(Guid.NewGuid(), Tenant, new(2026, 7, 1), new(2026, 6, 30), Generator, Now, true, "", "{}")).Code.ShouldBe("reporting.period_invalid");
    }
}
