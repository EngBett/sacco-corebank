using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Domain;

/// <summary>
/// A member's device that has already completed SMS OTP verification once. While trusted, the
/// native mobile app can sign the member back in with just phone + PIN (ROPC) — no OTP round
/// trip — mirroring how Equity/KCB/M-Pesa apps remember a device after its first verification.
/// A member exit or a suspicious-activity response revokes it (<see cref="Revoke"/>); revoking
/// forces the next sign-in on that device back through OTP.
/// </summary>
public class MemberTrustedDevice : TenantEntity
{
    private MemberTrustedDevice() { }

    public Guid MemberId { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;
    public string? DeviceName { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public DateTimeOffset TrustedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static MemberTrustedDevice Create(Guid tenantId, Guid memberId, string deviceId, string? deviceName, string platform, DateTimeOffset now) => new()
    {
        Id = Ids.New(),
        TenantId = tenantId,
        MemberId = memberId,
        DeviceId = deviceId,
        DeviceName = deviceName,
        Platform = platform,
        TrustedAt = now,
        LastUsedAt = now,
    };

    public bool IsTrusted(DateTimeOffset now) => RevokedAt is null;
    public void TouchLastUsed(DateTimeOffset now) => LastUsedAt = now;
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
