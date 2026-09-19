using Sacco.Modules.Identity.Domain;
using Shouldly;

namespace Sacco.UnitTests.Identity;

public sealed class MemberOtpChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    private static MemberOtpChallenge NewChallenge(string code = "123456") =>
        MemberOtpChallenge.Create(Guid.NewGuid(), Guid.NewGuid(), "device-1", "254712345678", code, Now);

    [Fact]
    public void Correct_code_is_consumed_exactly_once()
    {
        var challenge = NewChallenge();
        challenge.TryConsume("123456", Now).ShouldBeTrue();
        challenge.TryConsume("123456", Now).ShouldBeFalse("a consumed challenge can't be redeemed twice");
    }

    [Fact]
    public void Wrong_code_fails_but_counts_toward_the_attempt_cap()
    {
        var challenge = NewChallenge();
        for (var i = 0; i < MemberOtpChallenge.MaxAttempts - 1; i++) challenge.TryConsume("000000", Now).ShouldBeFalse();
        challenge.IsUsable(Now).ShouldBeTrue("one attempt remains");
        challenge.TryConsume("000000", Now).ShouldBeFalse();
        challenge.IsUsable(Now).ShouldBeFalse("attempts exhausted");
        challenge.TryConsume("123456", Now).ShouldBeFalse("even the right code fails once attempts are exhausted");
    }

    [Fact]
    public void Expired_challenge_cannot_be_consumed()
    {
        var challenge = NewChallenge();
        challenge.TryConsume("123456", Now + MemberOtpChallenge.Lifetime + TimeSpan.FromSeconds(1)).ShouldBeFalse();
    }

    [Fact]
    public void Generated_codes_are_always_six_digits()
    {
        var random = new Random(1);
        for (var i = 0; i < 50; i++)
        {
            var code = MemberOtpChallenge.GenerateCode(random);
            code.Length.ShouldBe(6);
            code.ShouldMatch("^[0-9]{6}$");
        }
    }
}
