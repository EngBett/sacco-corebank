using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Auth;

namespace Sacco.Modules.Identity.Application;

/// <summary>Answers "who holds this permission" for cross-module fan-out. Same query shape as <see cref="PermissionResolver"/>, inverted.</summary>
public sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyList<Guid>> UsersWithPermissionAsync(Guid tenantId, string permission, CancellationToken ct)
    {
        var roleIds = db.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.Permissions.Any(p => p.Permission == permission)).Select(r => r.Id);
        return await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive && u.Roles.Any(ur => roleIds.Contains(ur.RoleId)))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    public async Task<UserContact?> GetContactAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == userId && x.IsActive, ct);
        return u is null ? null : new UserContact(u.Id, u.DisplayName, u.Email, u.PhoneNumber);
    }
}
