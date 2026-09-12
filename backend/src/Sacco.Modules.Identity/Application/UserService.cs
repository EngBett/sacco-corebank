using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Application;

public sealed record AuthenticatedUser(Guid Id, string UserName, string DisplayName, string Email, Guid TenantId, IReadOnlyList<string> RoleNames);

public sealed class UserService(IdentityDbContext db, TenantContext tenant, IClock clock, IAuditLogger audit, PermissionResolver permissions)
{
    private static readonly PasswordHasher<StaffUser> Hasher = new();

    /// <summary>Validates credentials for the given tenant. Returns null on any failure (same timing/shape whether the user exists or not).</summary>
    public async Task<AuthenticatedUser?> AuthenticateAsync(Guid tenantId, string userName, string password, CancellationToken ct)
    {
        userName = userName.Trim().ToLowerInvariant();
        BindTenant(tenantId); // token/login endpoints are tenant-agnostic routes; the credentials name the tenant
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserName == userName, ct);
        if (user is null || !user.IsActive)
        {
            Hasher.HashPassword(null!, password); // burn comparable time
            return null;
        }
        var now = clock.UtcNow;
        if (user.IsLockedOut(now)) return null;

        var verdict = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin(now);
            await db.SaveChangesAsync(ct);
            return null;
        }
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
            user.SetPasswordHash(Hasher.HashPassword(user, password), user.MustChangePassword);

        user.RecordSuccessfulLogin(now);
        await db.SaveChangesAsync(ct);

        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roleNames = await db.Roles.IgnoreQueryFilters().Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
        return new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Email, user.TenantId, roleNames);
    }

    public async Task<AuthenticatedUser?> FindActiveAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        BindTenant(tenantId);
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId && u.IsActive, ct);
        if (user is null) return null;
        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roleNames = await db.Roles.IgnoreQueryFilters().Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
        return new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Email, user.TenantId, roleNames);
    }

    public async Task<StaffUser> CreateAsync(Guid? id, string userName, string email, string displayName, string? phone, string password, IReadOnlyList<Guid> roleIds, Guid createdBy, bool mustChangePassword, CancellationToken ct)
    {
        ValidatePassword(password);
        var user = StaffUser.Create(id ?? Ids.New(), tenant.TenantId, userName, email, displayName, phone, string.Empty, createdBy, clock.UtcNow, mustChangePassword);
        user.SetPasswordHash(Hasher.HashPassword(user, password), mustChangePassword);
        await EnsureRolesExist(roleIds, ct);
        user.SetRoles(roleIds);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new ConflictException("identity.user.duplicate", $"A user named '{userName}' already exists."); }
        await audit.RecordAsync(new AuditEvent("identity.user.created", nameof(StaffUser), user.Id.ToString(), createdBy, $$"""{"userName":"{{user.UserName}}"}"""), ct);
        return user;
    }

    public async Task SetRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, Guid byUser, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        await EnsureRolesExist(roleIds, ct);
        db.AddRange(user.SetRoles(roleIds));
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(tenant.TenantId, userId);
        await audit.RecordAsync(new AuditEvent("identity.user.roles_changed", nameof(StaffUser), userId.ToString(), byUser, $$"""{"roles":[{{string.Join(",", roleIds.Select(r => $"\"{r}\""))}}]}"""), ct);
    }

    public async Task SetActiveAsync(Guid userId, bool active, Guid byUser, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        if (userId == byUser && !active) throw new DomainRuleException("identity.user.self_deactivate", "You cannot deactivate your own account.");
        if (active) user.Reactivate(); else user.Deactivate();
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(tenant.TenantId, userId);
        await audit.RecordAsync(new AuditEvent(active ? "identity.user.reactivated" : "identity.user.deactivated", nameof(StaffUser), userId.ToString(), byUser), ct);
    }

    public async Task ResetPasswordAsync(Guid userId, string newPassword, Guid byUser, CancellationToken ct)
    {
        ValidatePassword(newPassword);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        user.SetPasswordHash(Hasher.HashPassword(user, newPassword), mustChange: byUser != userId);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.user.password_reset", nameof(StaffUser), userId.ToString(), byUser), ct);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        ValidatePassword(newPassword);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw new DomainRuleException("identity.user.wrong_password", "The current password is incorrect.");
        user.SetPasswordHash(Hasher.HashPassword(user, newPassword), mustChange: false);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.user.password_changed", nameof(StaffUser), userId.ToString(), userId), ct);
    }

    /// <summary>Binds the ambient tenant for authentication flows that arrive on tenant-agnostic routes (/connect/token, /account/login).</summary>
    private void BindTenant(Guid tenantId)
    {
        if (!tenant.HasTenant) tenant.Set(tenantId, string.Empty);
        else if (tenant.TenantId != tenantId) throw new ForbiddenException("Credentials belong to a different tenant than the current request.");
    }

    private async Task EnsureRolesExist(IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        if (roleIds.Count == 0) return;
        var found = await db.Roles.Where(r => roleIds.Contains(r.Id)).CountAsync(ct);
        if (found != roleIds.Distinct().Count())
            throw new NotFoundException("Role", string.Join(",", roleIds));
    }

    public static void ValidatePassword(string password)
    {
        if (password.Length < 10 || !password.Any(char.IsDigit) || !password.Any(char.IsLetter))
            throw new DomainRuleException("identity.password.weak", "Password must be at least 10 characters and contain letters and digits.");
    }
}
