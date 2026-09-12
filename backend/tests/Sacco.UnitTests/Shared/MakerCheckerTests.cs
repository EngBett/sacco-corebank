using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Shared;

public class MakerCheckerTests
{
    [Fact]
    public void Same_user_cannot_initiate_and_approve()
    {
        var user = Guid.NewGuid();
        Should.Throw<MakerCheckerViolationException>(() => MakerChecker.EnsureDistinct(user, user, "test"));
    }

    [Fact]
    public void Distinct_users_pass()
    {
        Should.NotThrow(() => MakerChecker.EnsureDistinct(Guid.NewGuid(), Guid.NewGuid(), "test"));
    }

    [Fact]
    public void Missing_initiator_or_approver_is_rejected()
    {
        Should.Throw<DomainRuleException>(() => MakerChecker.EnsureDistinct(Guid.Empty, Guid.NewGuid(), "test"));
        Should.Throw<DomainRuleException>(() => MakerChecker.EnsureDistinct(Guid.NewGuid(), Guid.Empty, "test"));
    }

    [Fact]
    public void Deterministic_ids_are_stable_and_distinct()
    {
        Ids.Deterministic("member:M00001").ShouldBe(Ids.Deterministic("member:M00001"));
        Ids.Deterministic("member:M00001").ShouldNotBe(Ids.Deterministic("member:M00002"));
    }
}
