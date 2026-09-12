using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Auth;

namespace Sacco.Modules.Identity.Application;

/// <summary>
/// Resolves a user's effective permissions from their roles, cached briefly per (tenant, user).
/// Role or assignment changes call <see cref="Invalidate"/> so revocation is immediate (ADR 0005).
/// </summary>
public sealed class PermissionResolver(IdentityDbContext db, IMemoryCache cache) : IPermissionResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static string Key(Guid tenantId, Guid userId) => $"perms:{tenantId:N}:{userId:N}";

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var result = await cache.GetOrCreateAsync(Key(tenantId, userId), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.TenantId == tenantId && u.IsActive, ct);
            if (!active) return new HashSet<string>();
            var permissions = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.TenantId == tenantId)
                .SelectMany(u => u.Roles)
                .Join(db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (ur, r) => r)
                .SelectMany(r => r.Permissions.Select(p => p.Permission))
                .Distinct()
                .ToListAsync(ct);
            return permissions.ToHashSet();
        });
        return result ?? new HashSet<string>();
    }

    public void Invalidate(Guid tenantId, Guid userId) => cache.Remove(Key(tenantId, userId));

    /// <summary>Called when a role's permissions change: every member of the role must be re-resolved.</summary>
    public async Task InvalidateRoleMembersAsync(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        var userIds = await db.Users.AsNoTracking().Where(u => u.Roles.Any(r => r.RoleId == roleId)).Select(u => u.Id).ToListAsync(ct);
        foreach (var id in userIds) Invalidate(tenantId, id);
    }
}
