using System.Security.Cryptography;
using System.Text;
using Sacco.Modules.Identity.Application.Mfa;
using Sacco.Modules.Identity.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Identity;

public class TotpTests
{
    // RFC 6238 appendix B, SHA-1 seed "12345678901234567890"; authenticator apps show the last six digits.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    public void Matches_the_rfc_6238_test_vectors(long unixTime, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixTime));
        Totp.Compute(RfcSecret, step, digits: 8).ShouldBe(expected);
        Totp.Compute(RfcSecret, step).ShouldBe(expected[2..]);
    }

    [Fact]
    public void Accepts_the_current_and_adjacent_steps_only()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.StepAt(now);
        Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step), now).ShouldBe(step);
        Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step - 1), now).ShouldBe(step - 1);
        Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step + 1), now).ShouldBe(step + 1);
        Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, step - 2), now).ShouldBeNull();
        Totp.Verify(RfcSecret, "12345", now).ShouldBeNull();
    }

    [Fact]
    public void Tolerates_spaces_in_a_typed_code()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var code = Totp.Compute(RfcSecret, Totp.StepAt(now));
        Totp.Verify(RfcSecret, $"{code[..3]} {code[3..]}", now).ShouldNotBeNull();
    }

    [Fact]
    public void Base32_round_trips_and_matches_the_rfc_4648_vector()
    {
        Totp.Base32Encode(Encoding.ASCII.GetBytes("foobar")).ShouldBe("MZXW6YTBOI");
        var secret = RandomNumberGenerator.GetBytes(20);
        Totp.Base32Decode(Totp.FormatForManualEntry(secret)).ShouldBe(secret);
    }

    [Fact]
    public void Provisioning_uri_carries_issuer_account_and_secret()
    {
        var uri = Totp.ProvisioningUri("Icodeio SACCO", "manager", Encoding.ASCII.GetBytes("foobar"));
        uri.ShouldStartWith("otpauth://totp/Icodeio%20SACCO%3Amanager?secret=MZXW6YTBOI&issuer=Icodeio%20SACCO");
        uri.ShouldContain("digits=6&period=30");
    }
}

public class TotpSecretProtectorTests
{
    private static MfaSettings Settings(string active, params string[] keyIds) => new()
    {
        ActiveKeyId = active,
        EncryptionKeys = keyIds.ToDictionary(id => id, _ => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
    };

    [Fact]
    public void Round_trips_and_never_stores_the_secret_in_the_clear()
    {
        var protector = new TotpSecretProtector(Settings("k1", "k1"));
        var secret = RandomNumberGenerator.GetBytes(20);
        var stored = protector.Protect(secret);
        stored.ShouldStartWith("k1:");
        stored.ShouldNotContain(Convert.ToBase64String(secret));
        protector.Unprotect(stored).ShouldBe(secret);
    }

    [Fact]
    public void Old_secrets_still_decrypt_after_rotating_to_a_new_key()
    {
        var settings = Settings("k1", "k1", "k2");
        var stored = new TotpSecretProtector(settings).Protect([1, 2, 3]);
        settings.ActiveKeyId = "k2";
        var rotated = new TotpSecretProtector(settings);
        rotated.Unprotect(stored).ShouldBe(new byte[] { 1, 2, 3 });
        rotated.Protect([1, 2, 3]).ShouldStartWith("k2:");
    }

    [Fact]
    public void A_tampered_value_or_the_wrong_key_is_rejected()
    {
        var stored = new TotpSecretProtector(Settings("k1", "k1")).Protect([9, 9, 9, 9]);
        var tampered = stored[..^4] + (stored[^4] == 'A' ? "B" : "A") + stored[^3..];
        Should.Throw<CryptographicException>(() => new TotpSecretProtector(Settings("k1", "k1")).Unprotect(stored)); // different random key
        Should.Throw<Exception>(() => new TotpSecretProtector(Settings("k1", "k1")).Unprotect(tampered));
    }

    [Fact]
    public void Refuses_to_start_without_a_usable_active_key()
    {
        Should.Throw<InvalidOperationException>(() => new TotpSecretProtector(new MfaSettings()));
        Should.Throw<InvalidOperationException>(() => new TotpSecretProtector(Settings("missing", "k1")));
    }
}

public class StaffUserMfaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private static StaffUser Invited() => StaffUser.CreateInvited(Guid.NewGuid(), Guid.NewGuid(), "fmuthoni", "faith@example.co.ke", "Faith", null, Guid.NewGuid(), Now);

    [Fact]
    public void An_invited_user_is_not_active_until_they_choose_a_password()
    {
        var user = Invited();
        user.Status.ShouldBe(StaffUserStatus.Invited);
        user.IsActivated.ShouldBeFalse();
        user.Activate("hash", Now);
        user.Status.ShouldBe(StaffUserStatus.Active);
        Should.Throw<DomainRuleException>(() => user.Activate("again", Now)).Code.ShouldBe("identity.user.already_activated");
    }

    [Fact]
    public void Enrolment_keeps_one_pending_secret_until_confirmed_and_blocks_replayed_steps()
    {
        var user = Invited();
        var calls = 0;
        var first = user.EnsurePendingTotpSecret(() => $"secret-{++calls}");
        user.EnsurePendingTotpSecret(() => $"secret-{++calls}").ShouldBe(first);
        calls.ShouldBe(1);

        user.EnableTotp(Now, confirmedStep: 100);
        user.IsMfaEnabled.ShouldBeTrue();
        user.TryUseTotpStep(100).ShouldBeFalse("the code used to confirm set-up can't sign in again");
        user.TryUseTotpStep(101).ShouldBeTrue();
        user.TryUseTotpStep(101).ShouldBeFalse();

        user.ResetMfa();
        user.IsMfaEnabled.ShouldBeFalse();
        user.TotpPendingSecret.ShouldBeNull();
    }

    [Fact]
    public void Recovery_codes_are_unique_hashed_and_forgiving_about_formatting()
    {
        var (codes, raw) = StaffRecoveryCode.GenerateSet(Guid.NewGuid(), Guid.NewGuid(), Now, Guid.NewGuid);
        raw.Count.ShouldBe(StaffRecoveryCode.CodesPerUser);
        raw.Distinct().Count().ShouldBe(raw.Count);
        raw.ShouldAllBe(c => System.Text.RegularExpressions.Regex.IsMatch(c, "^[a-z2-9]{5}-[a-z2-9]{5}$"));
        codes.ShouldAllBe(c => !raw.Contains(c.CodeHash));
        StaffRecoveryCode.Hash(raw[0].ToUpperInvariant().Replace("-", " ")).ShouldBe(codes[0].CodeHash);
    }
}

public class StaffAccountTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_the_hash_is_stored_and_the_token_works_once_before_it_expires()
    {
        var (token, raw) = StaffAccountToken.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StaffTokenPurpose.PasswordReset, Guid.NewGuid(), Now);
        raw.Length.ShouldBeGreaterThanOrEqualTo(43);
        token.TokenHash.ShouldBe(StaffAccountToken.Hash(raw));
        token.TokenHash.ShouldNotContain(raw);
        token.IsUsable(Now.AddMinutes(59)).ShouldBeTrue();
        token.IsUsable(Now.AddMinutes(61)).ShouldBeFalse("reset links last 60 minutes");
        token.Consume(Now);
        token.IsUsable(Now).ShouldBeFalse();
    }

    [Fact]
    public void Activation_links_last_three_days()
    {
        var (token, _) = StaffAccountToken.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StaffTokenPurpose.Activation, Guid.NewGuid(), Now);
        token.ExpiresAt.ShouldBe(Now.AddHours(72));
    }
}

public class StaffInvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static StaffInvitation Proposal() =>
        StaffInvitation.Propose(Guid.NewGuid(), Guid.NewGuid(), " FMuthoni ", "Faith.Muthoni@Example.co.ke", "Faith Muthoni", null, [Guid.NewGuid()], Manager, Now);

    [Fact]
    public void A_proposal_normalises_its_identity_and_waits_for_approval()
    {
        var invitation = Proposal();
        invitation.UserName.ShouldBe("fmuthoni");
        invitation.Email.ShouldBe("faith.muthoni@example.co.ke");
        invitation.Status.ShouldBe(StaffInvitationStatus.PendingApproval);
    }

    [Fact]
    public void The_proposer_cannot_approve_or_reject_their_own_proposal()
    {
        var invitation = Proposal();
        Should.Throw<MakerCheckerViolationException>(() => invitation.Approve(Manager, Now, selfApproved: false));
        Should.Throw<MakerCheckerViolationException>(() => invitation.Reject(Manager, "no", Now));
        invitation.Status.ShouldBe(StaffInvitationStatus.PendingApproval);
    }

    [Fact]
    public void Approval_then_sending_then_acceptance()
    {
        var invitation = Proposal();
        invitation.Approve(Admin, Now, selfApproved: false);
        var userId = Guid.NewGuid();
        invitation.MarkSent(userId, Now);
        invitation.Status.ShouldBe(StaffInvitationStatus.Sent);
        invitation.UserId.ShouldBe(userId);
        invitation.MarkAccepted(Now);
        invitation.Status.ShouldBe(StaffInvitationStatus.Accepted);
        Should.Throw<DomainRuleException>(() => invitation.Revoke(Admin, Now));
    }

    [Fact]
    public void A_user_administrator_inviting_directly_approves_their_own_invitation()
    {
        var invitation = StaffInvitation.Propose(Guid.NewGuid(), Guid.NewGuid(), "bkamau", "b@example.co.ke", "B Kamau", null, [Guid.NewGuid()], Admin, Now);
        invitation.Approve(Admin, Now, selfApproved: true);
        invitation.DecidedByUserId.ShouldBe(Admin);
    }

    [Theory]
    [InlineData("ab", "a@example.co.ke", "identity.invitation.username_invalid")]
    [InlineData("has space", "a@example.co.ke", "identity.invitation.username_invalid")]
    [InlineData("valid.name", "not-an-email", "identity.invitation.email_invalid")]
    public void Rejects_bad_details(string userName, string email, string code)
    {
        Should.Throw<DomainRuleException>(() => StaffInvitation.Propose(Guid.NewGuid(), Guid.NewGuid(), userName, email, "Name", null, [Guid.NewGuid()], Manager, Now)).Code.ShouldBe(code);
    }

    [Fact]
    public void Needs_at_least_one_role()
    {
        Should.Throw<DomainRuleException>(() => StaffInvitation.Propose(Guid.NewGuid(), Guid.NewGuid(), "fmuthoni", "f@example.co.ke", "Faith", null, [], Manager, Now))
            .Code.ShouldBe("identity.invitation.roles_required");
    }
}
