using System.Security.Claims;
using Open.IdentityServer.Models;
using Open.IdentityServer.Validation;
using Sacco.Modules.Identity.Application;

namespace Sacco.Modules.Identity.IdentityServer;

/// <summary>
/// Resource-owner password grant for first-party clients only (demo CLI, integration tests,
/// possibly the mobile app's native login). The tenant comes from the extra `tenant` form
/// field or from acr_values=tenant:&lt;slug&gt;.
/// </summary>
public sealed class SaccoPasswordValidator(UserService users, ITenantLookup tenants) : IResourceOwnerPasswordValidator
{
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

        var user = await users.AuthenticateAsync(tenant.Value.Id, context.UserName, context.Password, CancellationToken.None);
        if (user is null)
        {
            context.Result = new GrantValidationResult(TokenRequestErrors.InvalidGrant, "invalid credentials");
            return;
        }

        context.Result = new GrantValidationResult(user.Id.ToString(), "pwd", SubjectClaims.For(tenant.Value.Id, tenant.Value.Slug), "local");
    }
}

/// <summary>Claims stamped on the IdentityServer subject so the profile service knows which tenant a sub belongs to.</summary>
public static class SubjectClaims
{
    public static IEnumerable<Claim> For(Guid tenantId, string slug) =>
    [
        new("tenant_id", tenantId.ToString()),
        new(IdentityServerConfig.TenantClaim, slug),
    ];
}

/// <summary>Minimal tenant lookup the Identity module needs; implemented over the Platform module's directory in the host.</summary>
public interface ITenantLookup
{
    Task<(Guid Id, string Slug, string Name, string PrimaryColor)?> FindBySlugAsync(string slug, CancellationToken ct);
}
