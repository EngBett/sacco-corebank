namespace Sacco.Shared.Tenancy;

/// <summary>Lists tenants for background work that must run per tenant (schedulers). Implemented by the Platform module.</summary>
public interface ITenantEnumerator
{
    Task<IReadOnlyList<(Guid Id, string Slug)>> ListActiveAsync(CancellationToken ct);
}
