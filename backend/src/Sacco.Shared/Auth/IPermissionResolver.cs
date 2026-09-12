namespace Sacco.Shared.Auth;

/// <summary>
/// Resolves the effective permission set for a user in a tenant. Implemented by the Identity
/// module against a short-lived cache so that role changes take effect immediately, rather
/// than being baked into the JWT until expiry (ADR 0005).
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
