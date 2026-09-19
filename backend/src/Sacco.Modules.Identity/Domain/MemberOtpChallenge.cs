using Microsoft.AspNetCore.Identity;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Domain;

/// <summary>
/// A one-time SMS code issued to verify a member's phone on a device that hasn't completed
/// SMS OTP before (<see cref="MemberTrustedDevice"/>). Single-use, short-lived, capped attempts —
/// mirrors <see cref="MemberLogin"/>'s own lockout shape rather than inventing a new one.
/// </summary>
public class MemberOtpChallenge : TenantEntity
{
    private MemberOtpChallenge() { }

    public Guid MemberId { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public int Attempts { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly PasswordHasher<MemberOtpChallenge> Hasher = new();

    public static MemberOtpChallenge Create(Guid tenantId, Guid memberId, string deviceId, string phoneNumber, string code, DateTimeOffset now)
    {
        var challenge = new MemberOtpChallenge
        {
            Id = Ids.New(),
            TenantId = tenantId,
            MemberId = memberId,
            DeviceId = deviceId,
            PhoneNumber = phoneNumber,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        challenge.CodeHash = Hasher.HashPassword(challenge, code);
        return challenge;
    }

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt && Attempts < MaxAttempts;

    /// <summary>Verifies the supplied code and consumes the challenge on success. Every call — pass or
    /// fail — counts toward <see cref="MaxAttempts"/>, so a fixed number of guesses exhausts it.</summary>
    public bool TryConsume(string code, DateTimeOffset now)
    {
        if (!IsUsable(now)) return false;
        Attempts++;
        if (Hasher.VerifyHashedPassword(this, CodeHash, code) == PasswordVerificationResult.Failed) return false;
        ConsumedAt = now;
        return true;
    }

    public static string GenerateCode(Random random) => random.Next(0, 1_000_000).ToString("D6");
}
