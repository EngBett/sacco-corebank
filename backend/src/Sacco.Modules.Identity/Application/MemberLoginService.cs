using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Application;

public sealed record AuthenticatedMember(Guid LoginId, Guid MemberId, string DisplayName, string PhoneNumber, Guid TenantId);

/// <summary>Provisioning and authentication of member self-service logins (phone + PIN).</summary>
public sealed class MemberLoginService(IdentityDbContext db, TenantContext tenant, IClock clock, IAuditLogger audit, PermissionResolver permissions) : IMemberLoginProvisioner
{
    private static readonly PasswordHasher<MemberLogin> Hasher = new();

    public async Task ProvisionAsync(Guid memberId, string displayName, string phoneNumber, string pin, Guid byUserId, CancellationToken ct)
    {
        MemberLogin.ValidatePin(pin);
        var phone = MemberLogin.NormalisePhone(phoneNumber);
        var clash = await db.MemberLogins.FirstOrDefaultAsync(l => l.PhoneNumber == phone && l.MemberId != memberId && l.IsActive, ct);
        if (clash is not null) throw new Sacco.Shared.Domain.ConflictException("identity.member_login.phone_taken", "Another member already uses this phone number to sign in.");

        var login = await db.MemberLogins.FirstOrDefaultAsync(l => l.MemberId == memberId, ct);
        if (login is null)
        {
            login = MemberLogin.Create(tenant.TenantId, memberId, displayName, phone, string.Empty, byUserId, clock.UtcNow);
            login.Reset(displayName, phone, Hasher.HashPassword(login, pin));
            db.MemberLogins.Add(login);
        }
        else login.Reset(displayName, phone, Hasher.HashPassword(login, pin));
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(tenant.TenantId, login.Id);
        await audit.RecordAsync(new AuditEvent("identity.member_login.provisioned", nameof(MemberLogin), login.Id.ToString(), byUserId, $$"""{"memberId":"{{memberId}}","phone":"{{phone}}"}"""), ct);
    }

    public async Task DeactivateAsync(Guid memberId, Guid byUserId, CancellationToken ct)
    {
        var login = await db.MemberLogins.FirstOrDefaultAsync(l => l.MemberId == memberId, ct);
        if (login is null || !login.IsActive) return;
        login.Deactivate();
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(tenant.TenantId, login.Id);
        await audit.RecordAsync(new AuditEvent("identity.member_login.deactivated", nameof(MemberLogin), login.Id.ToString(), byUserId, $$"""{"memberId":"{{memberId}}"}"""), ct);
    }

    public Task<bool> IsEnabledAsync(Guid memberId, CancellationToken ct) => db.MemberLogins.AsNoTracking().AnyAsync(l => l.MemberId == memberId && l.IsActive, ct);

    /// <summary>Phone + PIN check for the given tenant; null on any failure (same shape whether the login exists or not).</summary>
    public async Task<AuthenticatedMember?> AuthenticateAsync(Guid tenantId, string phoneNumber, string pin, CancellationToken ct)
    {
        string phone;
        try { phone = MemberLogin.NormalisePhone(phoneNumber); } catch (Sacco.Shared.Domain.DomainRuleException) { return null; }
        var login = await db.MemberLogins.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.TenantId == tenantId && l.PhoneNumber == phone, ct);
        if (login is null || !login.IsActive) { Hasher.HashPassword(null!, pin); return null; }
        var now = clock.UtcNow;
        if (login.IsLockedOut(now)) return null;
        if (Hasher.VerifyHashedPassword(login, login.PinHash, pin) == PasswordVerificationResult.Failed)
        {
            login.RecordFailedLogin(now);
            await db.SaveChangesAsync(ct);
            return null;
        }
        login.RecordSuccessfulLogin(now);
        await db.SaveChangesAsync(ct);
        return new AuthenticatedMember(login.Id, login.MemberId, login.DisplayName, login.PhoneNumber, login.TenantId);
    }

    public enum PinCheck { Valid, Invalid, LockedOut }

    /// <summary>
    /// Re-checks the signed-in member's PIN (e.g. before showing balances). Failures share the sign-in lockout counter,
    /// so an unlocked phone can't be used to guess the PIN either.
    /// </summary>
    public async Task<PinCheck> VerifyPinAsync(Guid loginId, string pin, CancellationToken ct)
    {
        var login = await db.MemberLogins.FirstOrDefaultAsync(l => l.Id == loginId && l.IsActive, ct)
                    ?? throw new Sacco.Shared.Domain.NotFoundException("Member login", loginId);
        var now = clock.UtcNow;
        if (login.IsLockedOut(now)) return PinCheck.LockedOut;
        if (Hasher.VerifyHashedPassword(login, login.PinHash, pin ?? "") == PasswordVerificationResult.Failed)
        {
            login.RecordFailedLogin(now);
            await db.SaveChangesAsync(ct);
            if (!login.IsLockedOut(now)) return PinCheck.Invalid;
            await audit.RecordAsync(new AuditEvent("identity.member_login.locked_out", nameof(MemberLogin), login.Id.ToString(), login.Id, """{"via":"pin_check"}"""), ct);
            return PinCheck.LockedOut;
        }
        login.RecordSuccessfulPinCheck();
        await db.SaveChangesAsync(ct);
        return PinCheck.Valid;
    }

    public async Task<AuthenticatedMember?> FindActiveAsync(Guid tenantId, Guid loginId, CancellationToken ct)
    {
        var l = await db.MemberLogins.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == loginId && x.IsActive, ct);
        return l is null ? null : new AuthenticatedMember(l.Id, l.MemberId, l.DisplayName, l.PhoneNumber, l.TenantId);
    }
}
