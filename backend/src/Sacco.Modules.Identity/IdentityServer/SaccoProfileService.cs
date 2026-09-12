using System.Security.Claims;
using Open.IdentityServer.Models;
using Open.IdentityServer.Services;
using Sacco.Modules.Identity.Application;

namespace Sacco.Modules.Identity.IdentityServer;

/// <summary>
/// Emits light identity claims only (name, email, roles, tenant). Permissions are deliberately
/// NOT in the token — they are resolved server-side per request so revocation is immediate.
/// </summary>
public sealed class SaccoProfileService(UserService users) : IProfileService
{
    public async Task GetProfileDataAsync(ProfileDataRequestContext context)
    {
        var (tenantId, userId) = Parse(context.Subject);
        if (tenantId is null || userId is null) return;
        var user = await users.FindActiveAsync(tenantId.Value, userId.Value, CancellationToken.None);
        if (user is null) return;

        var claims = new List<Claim>
        {
            new("name", user.DisplayName),
            new("preferred_username", user.UserName),
            new("email", user.Email),
            new(IdentityServerConfig.TenantClaim, context.Subject.FindFirst(IdentityServerConfig.TenantClaim)?.Value ?? string.Empty),
        };
        claims.AddRange(user.RoleNames.Select(r => new Claim("role", r)));
        context.IssuedClaims.AddRange(claims);
    }

    public async Task IsActiveAsync(IsActiveContext context)
    {
        var (tenantId, userId) = Parse(context.Subject);
        context.IsActive = tenantId is not null && userId is not null
                           && await users.FindActiveAsync(tenantId.Value, userId.Value, CancellationToken.None) is not null;
    }

    private static (Guid? tenantId, Guid? userId) Parse(ClaimsPrincipal subject)
    {
        var sub = subject.FindFirst("sub")?.Value;
        var tenant = subject.FindFirst("tenant_id")?.Value;
        return (Guid.TryParse(tenant, out var t) ? t : null, Guid.TryParse(sub, out var u) ? u : null);
    }
}
