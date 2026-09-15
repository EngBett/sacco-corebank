using Sacco.Modules.Identity.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Identity;

public sealed class MemberLoginTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0712345678", "254712345678")]
    [InlineData("+254 712 345 678", "254712345678")]
    [InlineData("254712345678", "254712345678")]
    public void Phone_numbers_are_normalised_to_e164_digits(string input, string expected) => MemberLogin.NormalisePhone(input).ShouldBe(expected);

    [Theory]
    [InlineData("12345")] [InlineData("44712345678")] [InlineData("")]
    public void Non_kenyan_or_malformed_numbers_are_rejected(string input)
        => Should.Throw<DomainRuleException>(() => MemberLogin.NormalisePhone(input)).Code.ShouldBe("identity.member_login.phone_invalid");

    [Theory]
    [InlineData("123")] [InlineData("1234567")] [InlineData("12a4")]
    public void Pin_must_be_4_to_6_digits(string pin) => Should.Throw<DomainRuleException>(() => MemberLogin.ValidatePin(pin)).Code.ShouldBe("identity.member_login.pin_invalid");

    [Theory]
    [InlineData("1234")] [InlineData("0000")] [InlineData("7777")]
    public void Predictable_pins_are_refused(string pin) => Should.Throw<DomainRuleException>(() => MemberLogin.ValidatePin(pin)).Code.ShouldBe("identity.member_login.pin_weak");

    [Fact]
    public void Lockout_after_repeated_failures_then_clears_on_success()
    {
        var login = MemberLogin.Create(Guid.NewGuid(), Guid.NewGuid(), "Wanjiru Kamau", "0712345678", "hash", Guid.NewGuid(), Now);
        for (var i = 0; i < MemberLogin.MaxFailedAttempts; i++) login.RecordFailedLogin(Now);
        login.IsLockedOut(Now).ShouldBeTrue();
        login.IsLockedOut(Now + MemberLogin.LockoutDuration).ShouldBeFalse();
        login.RecordSuccessfulLogin(Now.AddHours(1));
        login.IsLockedOut(Now.AddHours(1)).ShouldBeFalse();
        login.LastLoginAt.ShouldBe(Now.AddHours(1));
    }
}
