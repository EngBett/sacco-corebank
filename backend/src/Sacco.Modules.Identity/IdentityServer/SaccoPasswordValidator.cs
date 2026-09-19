using System.Security.Claims;
using Open.IdentityServer.Models;
using Open.IdentityServer.Validation;
using Microsoft.Extensions.Hosting;
using Sacco.Modules.Identity.Application;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.IdentityServer;

/// <summary>
/// Resource-owner password grant for first-party clients only: <c>sacco-cli</c> (demo/integration
/// tests, staff or member) and <c>mobile</c> (the native app's phone+PIN sign-in, member only — see
/// ADR 0008's 2026-09-16 update). The tenant comes from the extra `tenant` form field or from
/// acr_values=tenant:&lt;slug&gt;, since <c>/connect/*</c> is tenant-agnostic in
/// <c>TenantResolutionMiddleware</c> and never sets the ambient tenant for us.
/// </summary>
public sealed class SaccoPasswordValidator(UserService users, MemberLoginService memberLogins, MemberOtpService memberOtp, ITenantLookup tenants, TenantContext tenantContext, IHostEnvironment env) : IResourceOwnerPasswordValidator
{
    /// <summary>The only client a public, no-secret mobile binary may authenticate as — staff credentials must never be
    /// checkable through it, so it never even attempts the staff path below.</summary>
    private const string MobileClientId = "mobile";

    public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
    {
        var slug = context.Request.Raw["tenant"] ?? context.Request.Raw["acr_values"]?.Split(' ').FirstOrDefault(v => v.StartsWith("tenant:"))?["tenant:".Length..];
        if (string.IsNullOrWhiteSpace(slug))
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidRequest, "tenant is required");
            return;
        }
        var tenant = await tenants.FindBySlugAsync(slug, CancellationToken.None);
        if (tenant is null)
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant, "invalid credentials");
            return;
        }

        // /connect/* is tenant-agnostic in TenantResolutionMiddleware, so nothing has set the ambient
        // tenant yet — and every query below (member/staff lookup, RLS-protected tables) needs it set
        // for TenantConnectionInterceptor to publish the right app.tenant_id to Postgres.
        tenantContext.Set(tenant.Value.Id, tenant.Value.Slug);

        var isMobileClient = context.Request.ClientId == MobileClientId;

        // Staff must pass two-step verification, which only the interactive sign-in (/account/login) enforces (ADR 0016).
        // The password grant stays available to staff for the demo CLI and integration tests, never in Production.
        if (!isMobileClient && !env.IsProduction())
        {
            var user = await users.AuthenticateAsync(tenant.Value.Id, context.UserName, context.Password, CancellationToken.None);
            if (user is not null)
            {
                context.Result = new GrantValidationResult(user.Id.ToString(), "pwd", SubjectClaims.For(tenant.Value.Id, tenant.Value.Slug, user.BranchId), "local");
                return;
            }
        }

        var member = await memberLogins.AuthenticateAsync(tenant.Value.Id, context.UserName, context.Password, CancellationToken.None);
        if (member is null)
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant, "invalid credentials");
            return;
        }

        if (isMobileClient)
        {
            var deviceId = context.Request.Raw["device_id"];
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                context.Result = new GrantValidationResult(TokenRequestErrors.InvalidRequest, "device_id is required");
                return;
            }

            if (await memberOtp.IsDeviceTrustedAsync(tenant.Value.Id, member.MemberId, deviceId, CancellationToken.None))
            {
                await memberOtp.TouchDeviceAsync(tenant.Value.Id, member.MemberId, deviceId, CancellationToken.None);
            }
            else
            {
                var otp = context.Request.Raw["otp"];
                var deviceName = context.Request.Raw["device_name"];
                var platform = context.Request.Raw["platform"] ?? "unknown";
                var verified = !string.IsNullOrWhiteSpace(otp) &&
                    await memberOtp.VerifyOtpAndTrustDeviceAsync(tenant.Value.Id, member.MemberId, deviceId, otp, deviceName, platform, CancellationToken.None);
                if (!verified)
                {
                    context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant, "otp_required_or_invalid");
                    return;
                }
            }
        }

        context.Result = new GrantValidationResult(member.LoginId.ToString(), "pin", SubjectClaims.ForMember(tenant.Value.Id, tenant.Value.Slug, member.MemberId), "local");
    }
}

/// <summary>Claims stamped on the IdentityServer subject so the profile service knows which tenant a sub belongs to.</summary>
public static class SubjectClaims
{
    public const string BranchIdClaim = "branch_id";

    public static IEnumerable<Claim> For(Guid tenantId, string slug, Guid? branchId = null) =>
        branchId is { } branch
            ? [new Claim("tenant_id", tenantId.ToString()), new Claim(IdentityServerConfig.TenantClaim, slug), new Claim(BranchIdClaim, branch.ToString())]
            : [new Claim("tenant_id", tenantId.ToString()), new Claim(IdentityServerConfig.TenantClaim, slug)];

    public const string MemberIdClaim = "member_id";
    public static IEnumerable<Claim> ForMember(Guid tenantId, string slug, Guid memberId) => [.. For(tenantId, slug), new Claim(MemberIdClaim, memberId.ToString())];
}

/// <summary>Minimal tenant lookup the Identity module needs; implemented over the Platform module's directory in the host.</summary>
public interface ITenantLookup
{
    Task<(Guid Id, string Slug, string Name, string PrimaryColor, string? LogoUrl, string? FaviconUrl)?> FindBySlugAsync(string slug, CancellationToken ct);
}
