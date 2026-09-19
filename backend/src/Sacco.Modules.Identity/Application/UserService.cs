using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Application;

/// <param name="MfaEnabled">False until the user has set up their authenticator app; the interactive sign-in then requires set-up.</param>
public sealed record AuthenticatedUser(Guid Id, string UserName, string DisplayName, string Email, Guid TenantId, IReadOnlyList<string> RoleNames, bool MfaEnabled = false, Guid? BranchId = null);

public sealed class UserService(IdentityDbContext db, TenantContext tenant, IClock clock, IAuditLogger audit, PermissionResolver permissions)
{
    private static readonly PasswordHasher<StaffUser> Hasher = new();

    /// <summary>Validates credentials for the given tenant. Returns null on any failure (same timing/shape whether the user exists or not).</summary>
    public async Task<AuthenticatedUser?> AuthenticateAsync(Guid tenantId, string userName, string password, CancellationToken ct)
    {
        userName = userName.Trim().ToLowerInvariant();
        BindTenant(tenantId); // token/login endpoints are tenant-agnostic routes; the credentials name the tenant
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserName == userName, ct);
        // An invited user who hasn't activated has no password yet; treat them exactly like an unknown user.
        if (user is null || !user.IsActive || !user.IsActivated)
        {
            Hasher.HashPassword(null!, password); // burn comparable time
            return null;
        }
        var now = clock.UtcNow;
        if (user.IsLockedOut(now))
        {
            await audit.RecordAsync(new AuditEvent("identity.signin.locked_out", nameof(StaffUser), user.Id.ToString(), user.Id,
                AuditDetails.New().With("userName", user.UserName).ToJson(), Outcome: AuditOutcome.Denied), ct);
            return null;
        }

        var verdict = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin(now);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("identity.signin.failed", nameof(StaffUser), user.Id.ToString(), user.Id,
                AuditDetails.New().With("userName", user.UserName).With("lockedOut", user.IsLockedOut(now)).ToJson(), Outcome: AuditOutcome.Failure), ct);
            return null;
        }
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
            user.SetPasswordHash(Hasher.HashPassword(user, password), user.MustChangePassword);

        // With two-step verification on, the password is only half of the sign-in: the failed-attempt counter is cleared by
        // a correct authenticator code (StaffMfaService), otherwise alternating a known password with code guesses would
        // never reach the lockout.
        if (!user.IsMfaEnabled) user.RecordSuccessfulLogin(now);
        await db.SaveChangesAsync(ct);

        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roleNames = await db.Roles.IgnoreQueryFilters().Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
        return new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Email, user.TenantId, roleNames, user.IsMfaEnabled, user.BranchId);
    }

    public async Task<AuthenticatedUser?> FindActiveAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        BindTenant(tenantId);
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId && u.IsActive && u.ActivatedAt != null, ct);
        if (user is null) return null;
        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roleNames = await db.Roles.IgnoreQueryFilters().Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
        return new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Email, user.TenantId, roleNames, user.IsMfaEnabled, user.BranchId);
    }

    /// <summary>Creates an already-active user with a known password. Seed data and tests only — real staff are invited (StaffInvitationService).</summary>
    public async Task<StaffUser> CreateAsync(Guid? id, string userName, string email, string displayName, string? phone, string password, IReadOnlyList<Guid> roleIds, Guid createdBy, bool mustChangePassword, CancellationToken ct, Guid? branchId = null)
    {
        ValidatePassword(password);
        var user = StaffUser.Create(id ?? Ids.New(), tenant.TenantId, userName, email, displayName, phone, string.Empty, createdBy, clock.UtcNow, mustChangePassword);
        user.Activate(Hasher.HashPassword(user, password), clock.UtcNow);
        if (mustChangePassword) user.SetPasswordHash(user.PasswordHash, mustChange: true);
        user.SetBranch(branchId);
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

    /// <summary>Moves a user to another office (or clears the assignment). The branch is checked against this tenant's registry.</summary>
    public async Task SetBranchAsync(Guid userId, Guid? branchId, IBranchDirectory branches, Guid byUser, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        if (branchId is { } id) await branches.EnsureExistsAsync(id, ct);
        user.SetBranch(branchId);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.user.branch_changed", nameof(StaffUser), userId.ToString(), byUser, $$"""{"branchId":"{{branchId}}"}"""), ct);
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
