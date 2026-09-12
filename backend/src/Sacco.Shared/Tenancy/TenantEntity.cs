namespace Sacco.Shared.Tenancy;

/// <summary>Base for every tenant-scoped row. Multi-tenancy is a column + RLS, not schema-per-tenant (backend/CLAUDE.md).</summary>
public abstract class TenantEntity
{
    public Guid Id { get; protected set; }
    public Guid TenantId { get; protected set; }
}
