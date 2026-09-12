using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Application;

public sealed class RoleService(IdentityDbContext db, ITenantContext tenant, IAuditLogger audit, PermissionResolver permissions)
{
    public async Task<Role> CreateAsync(Guid? id, string name, string description, IReadOnlyList<string> perms, Guid byUser, bool isSystem, CancellationToken ct)
    {
        var role = Role.Create(id ?? Ids.New(), tenant.TenantId, name, description, perms, isSystem);
        db.Roles.Add(role);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new ConflictException("identity.role.duplicate", $"A role named '{name}' already exists."); }
        await audit.RecordAsync(new AuditEvent("identity.role.created", nameof(Role), role.Id.ToString(), byUser, $$"""{"name":"{{role.Name}}","permissions":{{perms.Count}}}"""), ct);
        return role;
    }

    public async Task<Role> UpdateAsync(Guid roleId, string name, string description, IReadOnlyList<string> perms, Guid byUser, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct) ?? throw new NotFoundException("Role", roleId);
        role.Rename(name, description);
        db.AddRange(role.SetPermissions(perms));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateRoleMembersAsync(tenant.TenantId, roleId, ct);
        await audit.RecordAsync(new AuditEvent("identity.role.updated", nameof(Role), role.Id.ToString(), byUser, $$"""{"name":"{{role.Name}}","permissions":{{perms.Count}}}"""), ct);
        return role;
    }

    public async Task DeleteAsync(Guid roleId, Guid byUser, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct) ?? throw new NotFoundException("Role", roleId);
        if (role.IsSystem) throw new DomainRuleException("identity.role.system", "System roles cannot be deleted.");
        await permissions.InvalidateRoleMembersAsync(tenant.TenantId, roleId, ct);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.role.deleted", nameof(Role), roleId.ToString(), byUser, $$"""{"name":"{{role.Name}}"}"""), ct);
    }
}
