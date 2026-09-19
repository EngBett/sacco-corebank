using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Notifications;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Application;

/// <summary>Configuration section <c>Identity:Accounts</c>: where emailed links point.</summary>
public sealed class StaffAccountSettings
{
    public const string SectionName = "Identity:Accounts";

    /// <summary>Public origin of this API, which serves /account/activate and /account/reset-password (e.g. https://auth.sacco.example).</summary>
    public string PublicOrigin { get; set; } = "http://localhost:5000";

    /// <summary>Staff portal address, linked from "account activated" and "password changed" pages and emails.</summary>
    public string PortalUrl { get; set; } = "http://localhost:3000";

    /// <summary>A second reset email for the same account isn't sent within this window (stops mailbox flooding).</summary>
    public TimeSpan PasswordResetThrottle { get; set; } = TimeSpan.FromMinutes(2);
}

public enum TokenCheck { Valid, Invalid }

/// <summary>
/// Activation of invited staff and self-service password reset (ADR 0016). Tokens are single-use, stored hashed and
/// short-lived; "forgot password" answers the same way whether or not the account exists.
/// </summary>
public sealed class StaffAccountService(
    IdentityDbContext db,
    TenantContext tenant,
    ITenantLookup tenants,
    IEmailSender email,
    StaffAccountSettings settings,
    IClock clock,
    IAuditLogger audit,
    ILogger<StaffAccountService> logger)
{
    private static readonly PasswordHasher<StaffUser> Hasher = new();

    /// <summary>Issues an activation link for an invited user (invalidating any earlier one) and emails it.</summary>
    public async Task SendActivationAsync(StaffUser user, Guid byUser, CancellationToken ct)
    {
        var raw = await IssueTokenAsync(user, StaffTokenPurpose.Activation, byUser, ct);
        var tenantName = await TenantNameAsync(ct);
        var link = Link("activate", raw);
        await SendAsync(user.Email, $"Activate your {tenantName} portal account", StaffEmails.Activation(tenantName, user.DisplayName, user.UserName, link, StaffAccountToken.ActivationLifetime), ct);
    }

    /// <summary>Looks up an unexpired, unused token for the ambient tenant.</summary>
    public async Task<StaffUser?> FindUserForTokenAsync(string rawToken, StaffTokenPurpose purpose, CancellationToken ct)
    {
        var token = await FindTokenAsync(rawToken, purpose, ct);
        if (token is null) return null;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        return user is { IsActive: true } && (purpose != StaffTokenPurpose.Activation || !user.IsActivated) ? user : null;
    }

    public async Task<TokenCheck> ActivateAsync(string rawToken, string password, CancellationToken ct)
    {
        UserService.ValidatePassword(password);
        var token = await FindTokenAsync(rawToken, StaffTokenPurpose.Activation, ct);
        var user = token is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (token is null || user is null || !user.IsActive || user.IsActivated) return TokenCheck.Invalid;

        var now = clock.UtcNow;
        user.Activate(Hasher.HashPassword(user, password), now);
        token.Consume(now);
        var invitation = await db.StaffInvitations.FirstOrDefaultAsync(i => i.UserId == user.Id && i.Status == StaffInvitationStatus.Sent, ct);
        invitation?.MarkAccepted(now);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.user.activated", nameof(StaffUser), user.Id.ToString(), user.Id, $$"""{"userName":"{{user.UserName}}"}"""), ct);
        return TokenCheck.Valid;
    }

    /// <summary>
    /// "Forgot password": emails a reset link to each active account whose user name or email matches. Silent about
    /// whether anything matched, and throttled per account.
    /// </summary>
    public async Task RequestPasswordResetAsync(string userNameOrEmail, CancellationToken ct)
    {
        var key = userNameOrEmail.Trim().ToLowerInvariant();
        if (key.Length == 0) return;
        var matches = await db.Users.Where(u => u.IsActive && u.ActivatedAt != null && (u.UserName == key || u.Email == key)).ToListAsync(ct);
        foreach (var user in matches)
        {
            var now = clock.UtcNow;
            var recent = await db.StaffAccountTokens.AnyAsync(t => t.UserId == user.Id && t.Purpose == StaffTokenPurpose.PasswordReset && t.CreatedAt > now - settings.PasswordResetThrottle, ct);
            if (recent)
            {
                logger.LogInformation("Password reset for staff user {UserId} throttled", user.Id);
                continue;
            }
            await SendPasswordResetAsync(user, byUser: user.Id, ct);
        }
    }

    /// <summary>Emails a password reset link. Also used by an administrator, who never sees or sets the password.</summary>
    public async Task SendPasswordResetAsync(StaffUser user, Guid byUser, CancellationToken ct)
    {
        if (!user.IsActivated)
            throw new DomainRuleException("identity.user.not_activated", "This user hasn't activated their account yet — resend the invitation instead.");
        var raw = await IssueTokenAsync(user, StaffTokenPurpose.PasswordReset, byUser, ct);
        var tenantName = await TenantNameAsync(ct);
        await SendAsync(user.Email, $"Reset your {tenantName} portal password", StaffEmails.PasswordReset(tenantName, user.DisplayName, user.UserName, Link("reset-password", raw), StaffAccountToken.PasswordResetLifetime, requestedByAdmin: byUser != user.Id), ct);
        await audit.RecordAsync(new AuditEvent("identity.user.password_reset_requested", nameof(StaffUser), user.Id.ToString(), byUser), ct);
    }

    public async Task<TokenCheck> ResetPasswordAsync(string rawToken, string password, CancellationToken ct)
    {
        UserService.ValidatePassword(password);
        var token = await FindTokenAsync(rawToken, StaffTokenPurpose.PasswordReset, ct);
        var user = token is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (token is null || user is null || !user.IsActive || !user.IsActivated) return TokenCheck.Invalid;

        var now = clock.UtcNow;
        user.SetPasswordHash(Hasher.HashPassword(user, password), mustChange: false);
        user.RecordSuccessfulLogin(now); // clears a lockout: proving control of the mailbox is enough to try again
        token.Consume(now);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.user.password_reset", nameof(StaffUser), user.Id.ToString(), user.Id), ct);

        var tenantName = await TenantNameAsync(ct);
        await SendAsync(user.Email, $"Your {tenantName} portal password was changed", StaffEmails.PasswordChanged(tenantName, user.DisplayName, settings.PortalUrl), ct);
        return TokenCheck.Valid;
    }

    private async Task<string> IssueTokenAsync(StaffUser user, StaffTokenPurpose purpose, Guid byUser, CancellationToken ct)
    {
        var now = clock.UtcNow;
        // Only the newest link of each kind works.
        var outstanding = await db.StaffAccountTokens.Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null).ToListAsync(ct);
        foreach (var old in outstanding) old.Consume(now);
        var (token, raw) = StaffAccountToken.Issue(Ids.New(), tenant.TenantId, user.Id, purpose, byUser, now);
        db.StaffAccountTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return raw;
    }

    private async Task<StaffAccountToken?> FindTokenAsync(string rawToken, StaffTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 100) return null;
        var hash = StaffAccountToken.Hash(rawToken.Trim());
        var token = await db.StaffAccountTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        return token is not null && token.IsUsable(clock.UtcNow) ? token : null;
    }

    private string Link(string page, string rawToken) =>
        $"{settings.PublicOrigin.TrimEnd('/')}/account/{page}?tenant={Uri.EscapeDataString(tenant.TenantSlug)}&token={Uri.EscapeDataString(rawToken)}";

    private async Task<string> TenantNameAsync(CancellationToken ct) =>
        (await tenants.FindBySlugAsync(tenant.TenantSlug, ct))?.Name ?? "SACCO";

    private async Task SendAsync(string to, string subject, string html, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(to, subject, html, null, ct);
        }
        catch (Exception ex)
        {
            // The link is already issued and audited; an administrator can resend. Don't turn a mail outage into a 500
            // that tells a "forgot password" caller the account exists.
            logger.LogError(ex, "Failed to send '{Subject}' to a staff user", subject);
        }
    }
}

/// <summary>Plain, table-free HTML emails: they render the same in Outlook, Gmail and Mailpit.</summary>
internal static class StaffEmails
{
    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static string Layout(string tenantName, string body) => $$"""
        <!doctype html><html><body style="margin:0;padding:24px;background:#f5f5f5;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#0a0a0a">
        <div style="max-width:520px;margin:0 auto;background:#ffffff;border:1px solid #e5e5e5;border-radius:12px;padding:28px">
        <p style="margin:0 0 20px;font-weight:600">{{E(tenantName)}}</p>
        {{body}}
        </div>
        <p style="max-width:520px;margin:16px auto 0;font-size:12px;color:#737373;text-align:center">This is an automated message from {{E(tenantName)}}. Please don't reply.</p>
        </body></html>
        """;

    private static string Button(string link, string label) =>
        $"""<p style="margin:24px 0"><a href="{E(link)}" style="display:inline-block;background:#0f766e;color:#ffffff;text-decoration:none;padding:10px 18px;border-radius:6px;font-weight:600">{E(label)}</a></p><p style="margin:0 0 16px;font-size:12px;color:#737373;word-break:break-all">Or paste this link into your browser: {E(link)}</p>""";

    private static string Hours(TimeSpan span) => span.TotalHours >= 1 ? $"{span.TotalHours:0} hours" : $"{span.TotalMinutes:0} minutes";

    public static string Activation(string tenantName, string displayName, string userName, string link, TimeSpan lifetime) => Layout(tenantName, $"""
        <h1 style="font-size:20px;margin:0 0 12px">Welcome, {E(displayName)}</h1>
        <p style="margin:0 0 12px;line-height:1.5">An account has been created for you on the {E(tenantName)} staff portal. Your user name is <strong>{E(userName)}</strong>.</p>
        <p style="margin:0;line-height:1.5">Choose a password to activate it. The first time you sign in you'll also set up two-step verification with Google Authenticator or Microsoft Authenticator.</p>
        {Button(link, "Activate account")}
        <p style="margin:0;font-size:13px;color:#737373">This link works once and expires in {Hours(lifetime)}. If you weren't expecting it, you can ignore this email.</p>
        """);

    public static string PasswordReset(string tenantName, string displayName, string userName, string link, TimeSpan lifetime, bool requestedByAdmin) => Layout(tenantName, $"""
        <h1 style="font-size:20px;margin:0 0 12px">Reset your password</h1>
        <p style="margin:0;line-height:1.5">Hi {E(displayName)}, {(requestedByAdmin ? "an administrator has asked for" : "we received a request to reset")} the password for <strong>{E(userName)}</strong>.</p>
        {Button(link, "Choose a new password")}
        <p style="margin:0;font-size:13px;color:#737373">This link works once and expires in {Hours(lifetime)}. If you didn't ask for this, ignore this email — your password stays the same.</p>
        """);

    public static string PasswordChanged(string tenantName, string displayName, string portalUrl) => Layout(tenantName, $"""
        <h1 style="font-size:20px;margin:0 0 12px">Your password was changed</h1>
        <p style="margin:0;line-height:1.5">Hi {E(displayName)}, the password for your {E(tenantName)} portal account was just changed.</p>
        <p style="margin:12px 0 0;line-height:1.5">If this wasn't you, contact your system administrator straight away.</p>
        {Button(portalUrl, "Go to the portal")}
        """);

    public static string MfaReset(string tenantName, string displayName) => Layout(tenantName, $"""
        <h1 style="font-size:20px;margin:0 0 12px">Two-step verification was reset</h1>
        <p style="margin:0;line-height:1.5">Hi {E(displayName)}, an administrator reset two-step verification on your {E(tenantName)} portal account. You'll set it up again with your authenticator app the next time you sign in.</p>
        <p style="margin:12px 0 0;line-height:1.5">If you didn't ask for this, contact your system administrator straight away.</p>
        """);
}
