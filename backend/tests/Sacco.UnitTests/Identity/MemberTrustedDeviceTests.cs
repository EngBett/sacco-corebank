using Sacco.Modules.Identity.Domain;
using Shouldly;

namespace Sacco.UnitTests.Identity;

public sealed class MemberTrustedDeviceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_freshly_trusted_device_is_trusted_until_revoked()
    {
        var device = MemberTrustedDevice.Create(Guid.NewGuid(), Guid.NewGuid(), "device-1", "Pixel 8", "android", Now);
        device.IsTrusted(Now).ShouldBeTrue();

        device.Revoke(Now.AddDays(1));
        device.IsTrusted(Now.AddDays(1)).ShouldBeFalse("a revoked device must go back through OTP on its next sign-in");
    }

    [Fact]
    public void Touching_last_used_does_not_affect_trust()
    {
        var device = MemberTrustedDevice.Create(Guid.NewGuid(), Guid.NewGuid(), "device-1", null, "ios", Now);
        device.TouchLastUsed(Now.AddHours(3));
        device.LastUsedAt.ShouldBe(Now.AddHours(3));
        device.IsTrusted(Now.AddHours(3)).ShouldBeTrue();
    }
}
