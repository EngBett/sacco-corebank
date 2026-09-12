using Sacco.Modules.Identity.Domain;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Identity;

public class RoleTests
{
    [Fact]
    public void Role_only_accepts_registered_permissions()
    {
        Should.Throw<DomainRuleException>(() => Role.Create(Guid.NewGuid(), Guid.NewGuid(), "X", "", ["loans.fly"])).Code.ShouldBe("identity.role.unknown_permission");
        var role = Role.Create(Guid.NewGuid(), Guid.NewGuid(), "Teller", "", [Permissions.Savings.Deposit, Permissions.Savings.Deposit]);
        role.Permissions.Count.ShouldBe(1);
    }

    [Fact]
    public void Set_permissions_replaces_the_bundle()
    {
        var role = Role.Create(Guid.NewGuid(), Guid.NewGuid(), "Teller", "", [Permissions.Savings.Deposit, Permissions.Savings.Withdraw]);
        role.SetPermissions([Permissions.Savings.Withdraw, Permissions.Loans.Repay]);
        role.Permissions.Select(p => p.Permission).ShouldBe([Permissions.Savings.Withdraw, Permissions.Loans.Repay], ignoreOrder: true);
    }

    [Fact]
    public void Lockout_after_repeated_failures()
    {
        var now = DateTimeOffset.UtcNow;
        var user = StaffUser.Create(Guid.NewGuid(), Guid.NewGuid(), "teller", "t@x.io", "T", null, "hash", Guid.NewGuid(), now, false);
        for (var i = 0; i < StaffUser.MaxFailedAttempts; i++) user.RecordFailedLogin(now);
        user.IsLockedOut(now).ShouldBeTrue();
        user.IsLockedOut(now + StaffUser.LockoutDuration + TimeSpan.FromSeconds(1)).ShouldBeFalse();
        user.RecordSuccessfulLogin(now);
        user.IsLockedOut(now).ShouldBeFalse();
    }

    [Fact]
    public void Every_permission_constant_is_registered_in_All()
    {
        var constants = typeof(Permissions).GetNestedTypes()
            .SelectMany(t => t.GetFields())
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        constants.ShouldNotBeEmpty();
        constants.ShouldAllBe(c => Permissions.IsKnown(c));
        Permissions.All.Select(p => p.Name).Distinct().Count().ShouldBe(Permissions.All.Count);
    }
}
