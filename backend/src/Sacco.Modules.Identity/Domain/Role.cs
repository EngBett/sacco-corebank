using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Domain;

/// <summary>A per-tenant bundle of granular permissions. Editable without a deployment (non-negotiable #7).</summary>
public class Role : TenantEntity
{
    private readonly List<RolePermission> _permissions = [];
    private Role() { }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    /// <summary>System roles (e.g. System Admin) cannot be deleted.</summary>
    public bool IsSystem { get; private set; }
    public IReadOnlyList<RolePermission> Permissions => _permissions;

    public static Role Create(Guid id, Guid tenantId, string name, string description, IEnumerable<string> permissions, bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainRuleException("identity.role.name_required", "Role name is required.");
        var role = new Role { Id = id, TenantId = tenantId, Name = name.Trim(), Description = description, IsSystem = isSystem };
        role.SetPermissions(permissions);
        return role;
    }

    public void Rename(string name, string description) { Name = name.Trim(); Description = description; }

    /// <summary>Replaces the bundle. Returns the newly added rows so a service can register them as Added on a tracked role.</summary>
    public IReadOnlyList<RolePermission> SetPermissions(IEnumerable<string> permissions)
    {
        var target = permissions.Select(p => p.Trim()).Distinct().ToList();
        var unknown = target.Where(p => !Sacco.Shared.Auth.Permissions.IsKnown(p)).ToList();
        if (unknown.Count > 0)
            throw new DomainRuleException("identity.role.unknown_permission", $"Unknown permission(s): {string.Join(", ", unknown)}.");
        _permissions.RemoveAll(p => !target.Contains(p.Permission));
        var added = target.Where(p => _permissions.All(x => x.Permission != p)).Select(p => new RolePermission(Id, p)).ToList();
        _permissions.AddRange(added);
        return added;
    }
}

public class RolePermission
{
    private RolePermission() { }
    internal RolePermission(Guid roleId, string permission) { RoleId = roleId; Permission = permission; }
    public Guid RoleId { get; private set; }
    public string Permission { get; private set; } = string.Empty;
}
