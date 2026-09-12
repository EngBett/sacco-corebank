namespace Sacco.Shared.Tenancy;

/// <summary>
/// The tenant (licensed SACCO) the current request/operation is scoped to. Resolved once per
/// request by middleware; every module DbContext filters by it, and the connection interceptor
/// publishes it to Postgres for row-level security.
/// </summary>
public interface ITenantContext
{
    bool HasTenant { get; }
    Guid TenantId { get; }
    string TenantSlug { get; }
}

/// <summary>Mutable, scoped implementation set by tenant-resolution middleware or by tools (seed, tests).</summary>
public sealed class TenantContext : ITenantContext
{
    private Guid _tenantId;
    private string _slug = string.Empty;

    public bool HasTenant => _tenantId != Guid.Empty;

    public Guid TenantId => HasTenant
        ? _tenantId
        : throw new InvalidOperationException("No tenant has been resolved for the current scope.");

    public string TenantSlug => HasTenant
        ? _slug
        : throw new InvalidOperationException("No tenant has been resolved for the current scope.");

    public void Set(Guid tenantId, string slug)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        _tenantId = tenantId;
        _slug = slug;
    }

    public void Clear()
    {
        _tenantId = Guid.Empty;
        _slug = string.Empty;
    }
}
