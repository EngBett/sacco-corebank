namespace Sacco.Shared.Tenancy;

/// <summary>Lists tenants for background work that must run per tenant (schedulers). Implemented by the Platform module.</summary>
public interface ITenantEnumerator
{
    Task<IReadOnlyList<(Guid Id, string Slug)>> ListActiveAsync(CancellationToken ct);

    /// <summary>Branding for a scheduled job that needs to put the tenant's name/logo on something it produces (an emailed report, a statement). Null if the tenant no longer exists.</summary>
    Task<TenantBrandingInfo?> GetBrandingAsync(Guid tenantId, CancellationToken ct);
}

public sealed record TenantBrandingInfo(string Name, string ShortName, string PrimaryColor, string SecondaryColor, string? LogoUrl, string SupportEmail);
