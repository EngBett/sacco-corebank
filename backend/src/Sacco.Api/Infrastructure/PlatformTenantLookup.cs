using Sacco.Modules.Identity.IdentityServer;
using Sacco.Modules.Platform.Application;

namespace Sacco.Api.Infrastructure;

/// <summary>Bridges the Identity module's tenant lookup onto the Platform module's directory (composition root only).</summary>
public sealed class PlatformTenantLookup(ITenantDirectory directory) : ITenantLookup
{
    public async Task<(Guid Id, string Slug, string Name, string PrimaryColor)?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        var t = await directory.FindBySlugAsync(slug, ct);
        return t is null || !t.IsActive ? null : (t.Id, t.Slug, t.Name, t.Branding.PrimaryColor);
    }
}
