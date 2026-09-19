using Microsoft.EntityFrameworkCore;
using QRCoder;
using Sacco.Modules.Identity.Application.Mfa;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Notifications;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Application;

public sealed record MfaEnrollment(string QrSvg, string ManualKey, string Issuer, string AccountName);

public enum MfaCheck { Valid, ValidRecoveryCode, Invalid, LockedOut }

/// <summary>
/// Authenticator-app (TOTP) two-step verification for staff (ADR 0016): mandatory, set up at the first sign-in, with ten
/// one-time recovery codes. Wrong codes count towards the same lockout as wrong passwords.
/// </summary>
public sealed class StaffMfaService(
    IdentityDbContext db,
    TenantContext tenant,
    ITenantLookup tenants,
    TotpSecretProtector protector,
    MfaSettings settings,
    IEmailSender email,
    IClock clock,
    IAuditLogger audit)
{
    public async Task<StaffUser> GetUserAsync(Guid userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);

    /// <summary>Starts (or resumes) set-up: the same secret is shown until it is confirmed, so reloading the page is harmless.</summary>
    public async Task<MfaEnrollment> BeginEnrollmentAsync(Guid userId, CancellationToken ct)
    {
        var user = await GetUserAsync(userId, ct);
        var hadPending = user.TotpPendingSecret is not null;
        var protectedSecret = user.EnsurePendingTotpSecret(() => protector.Protect(Totp.NewSecret()));
        if (!hadPending) await db.SaveChangesAsync(ct);

        var secret = protector.Unprotect(protectedSecret);
        var issuer = settings.IssuerOverride ?? (await tenants.FindBySlugAsync(tenant.TenantSlug, ct))?.Name ?? "SACCO";
        var uri = Totp.ProvisioningUri(issuer, user.UserName, secret);
        using var qr = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(qr).GetGraphic(new System.Drawing.Size(208, 208), "#0a0a0a", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
        return new MfaEnrollment(svg, Totp.FormatForManualEntry(secret), issuer, user.UserName);
    }

    /// <summary>Confirms set-up with a code from the app. Returns the recovery codes to show once, or null for a wrong code.</summary>
    public async Task<IReadOnlyList<string>?> ConfirmEnrollmentAsync(Guid userId, string code, CancellationToken ct)
    {
        var user = await GetUserAsync(userId, ct);
        if (user.TotpPendingSecret is null) throw new DomainRuleException("identity.mfa.not_started", "Two-step verification set-up hasn't been started.");
        var now = clock.UtcNow;
        var step = Totp.Verify(protector.Unprotect(user.TotpPendingSecret), code, now);
        if (step is null)
        {
            user.RecordFailedLogin(now);
            await db.SaveChangesAsync(ct);
            return null;
        }

        user.EnableTotp(now, step.Value);
        var raw = await ReplaceRecoveryCodesAsync(user, now, ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.user.mfa_enabled", nameof(StaffUser), user.Id.ToString(), user.Id), ct);
        return raw;
    }

    /// <summary>Checks a 6-digit authenticator code or a recovery code at sign-in.</summary>
    public async Task<MfaCheck> VerifyAsync(Guid userId, string code, CancellationToken ct)
    {
        var user = await GetUserAsync(userId, ct);
        var now = clock.UtcNow;
        if (!user.IsActive || !user.IsMfaEnabled) return MfaCheck.Invalid;
        if (user.IsLockedOut(now)) return MfaCheck.LockedOut;

        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (digits.Length == Totp.Digits && digits.Length == code.Trim().Replace(" ", "").Length)
        {
            var step = Totp.Verify(protector.Unprotect(user.TotpSecret!), digits, now);
            if (step is not null && user.TryUseTotpStep(step.Value))
            {
                user.RecordSuccessfulLogin(now);
                await db.SaveChangesAsync(ct);
                return MfaCheck.Valid;
            }
        }
        else
        {
            var hash = StaffRecoveryCode.Hash(code);
            var recovery = await db.StaffRecoveryCodes.FirstOrDefaultAsync(c => c.UserId == user.Id && c.CodeHash == hash && c.UsedAt == null, ct);
            if (recovery is not null)
            {
                recovery.Use(now);
                user.RecordSuccessfulLogin(now);
                await db.SaveChangesAsync(ct);
                await audit.RecordAsync(new AuditEvent("identity.user.recovery_code_used", nameof(StaffUser), user.Id.ToString(), user.Id), ct);
                return MfaCheck.ValidRecoveryCode;
            }
        }

        user.RecordFailedLogin(now);
        await db.SaveChangesAsync(ct);
        var lockedOut = user.IsLockedOut(now);
        await audit.RecordAsync(new AuditEvent(lockedOut ? "identity.signin.locked_out" : "identity.mfa.failed", nameof(StaffUser), user.Id.ToString(), user.Id,
            AuditDetails.New().With("userName", user.UserName).ToJson(), Outcome: lockedOut ? AuditOutcome.Denied : AuditOutcome.Failure), ct);
        return lockedOut ? MfaCheck.LockedOut : MfaCheck.Invalid;
    }

    public Task<int> RemainingRecoveryCodesAsync(Guid userId, CancellationToken ct) =>
        db.StaffRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null, ct);

    /// <summary>Administrator reset for a lost phone: the user sets up a new authenticator at their next sign-in.</summary>
    public async Task ResetAsync(Guid userId, Guid byUser, CancellationToken ct)
    {
        if (userId == byUser)
            throw new DomainRuleException("identity.mfa.self_reset", "Ask another administrator to reset your own two-step verification.");
        var user = await GetUserAsync(userId, ct);
        user.ResetMfa();
        await db.StaffRecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.user.mfa_reset", nameof(StaffUser), userId.ToString(), byUser), ct);
        if (user.IsActivated)
        {
            var tenantName = (await tenants.FindBySlugAsync(tenant.TenantSlug, ct))?.Name ?? "SACCO";
            try { await email.SendAsync(user.Email, $"Two-step verification was reset on your {tenantName} account", StaffEmails.MfaReset(tenantName, user.DisplayName), null, ct); }
            catch { /* the reset stands; the email is a courtesy */ }
        }
    }

    private async Task<List<string>> ReplaceRecoveryCodesAsync(StaffUser user, DateTimeOffset now, CancellationToken ct)
    {
        await db.StaffRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
        var (codes, raw) = StaffRecoveryCode.GenerateSet(tenant.TenantId, user.Id, now, Ids.New);
        db.StaffRecoveryCodes.AddRange(codes);
        return raw;
    }
}
