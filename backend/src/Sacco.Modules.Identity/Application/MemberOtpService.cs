using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Notifications;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Application;

/// <summary>
/// SMS-OTP device trust for the native mobile app's phone+PIN sign-in (ADR 0008 update). A device
/// that has never completed OTP for a member is untrusted; <see cref="SaccoPasswordValidator"/>
/// gates the ROPC grant on it for the <c>mobile</c> client. Tenant is always passed explicitly
/// (never read from ambient <c>ITenantContext</c>) because this runs inside the token-endpoint
/// pipeline, the same reasoning <see cref="MemberLoginService.AuthenticateAsync"/> already follows.
/// </summary>
public sealed class MemberOtpService(IdentityDbContext db, IClock clock, ISmsSender sms, IAuditLogger audit)
{
    public async Task<bool> IsDeviceTrustedAsync(Guid tenantId, Guid memberId, string deviceId, CancellationToken ct)
    {
        var device = await db.MemberTrustedDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.MemberId == memberId && d.DeviceId == deviceId, ct);
        return device is not null && device.IsTrusted(clock.UtcNow);
    }

    public async Task TouchDeviceAsync(Guid tenantId, Guid memberId, string deviceId, CancellationToken ct)
    {
        var device = await db.MemberTrustedDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.MemberId == memberId && d.DeviceId == deviceId, ct);
        if (device is null) return;
        device.TouchLastUsed(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sends a fresh SMS code unless the device is already trusted or an unexpired code was already
    /// sent for this device (reusing it instead of spamming SMS on every retry/resend tap).
    /// </summary>
    public async Task<(bool OtpRequired, int ExpiresInSeconds)> RequestOtpIfNeededAsync(Guid tenantId, Guid memberId, string deviceId, string phoneNumber, CancellationToken ct)
    {
        if (await IsDeviceTrustedAsync(tenantId, memberId, deviceId, ct)) return (false, 0);

        var now = clock.UtcNow;
        var existing = await FindActiveChallengeAsync(tenantId, memberId, deviceId, now, ct);
        if (existing is not null) return (true, (int)(existing.ExpiresAt - now).TotalSeconds);

        var code = MemberOtpChallenge.GenerateCode(Random.Shared);
        var challenge = MemberOtpChallenge.Create(tenantId, memberId, deviceId, phoneNumber, code, now);
        db.MemberOtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await sms.SendAsync(phoneNumber, $"Your SACCO verification code is {code}. It expires in 5 minutes. Never share this code with anyone.", ct);
        return (true, (int)MemberOtpChallenge.Lifetime.TotalSeconds);
    }

    /// <summary>Verifies the most recent active code for this device and, on success, trusts the
    /// device so future sign-ins skip OTP entirely. Every call — pass or fail — counts against the
    /// challenge's attempt cap via <see cref="MemberOtpChallenge.TryConsume"/>.</summary>
    public async Task<bool> VerifyOtpAndTrustDeviceAsync(Guid tenantId, Guid memberId, string deviceId, string code, string? deviceName, string platform, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var challenge = await FindActiveChallengeAsync(tenantId, memberId, deviceId, now, ct);
        if (challenge is null) return false;
        if (!challenge.TryConsume(code, now))
        {
            await db.SaveChangesAsync(ct);
            return false;
        }

        var device = await db.MemberTrustedDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.MemberId == memberId && d.DeviceId == deviceId, ct);
        if (device is null)
        {
            device = MemberTrustedDevice.Create(tenantId, memberId, deviceId, deviceName, platform, now);
            db.MemberTrustedDevices.Add(device);
        }
        else device.TouchLastUsed(now);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.member_device.trusted", nameof(MemberTrustedDevice), device.Id.ToString(), memberId, $$"""{"memberId":"{{memberId}}","deviceId":"{{deviceId}}"}"""), ct);
        return true;
    }

    private Task<MemberOtpChallenge?> FindActiveChallengeAsync(Guid tenantId, Guid memberId, string deviceId, DateTimeOffset now, CancellationToken ct) =>
        db.MemberOtpChallenges.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.MemberId == memberId && c.DeviceId == deviceId && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
