using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Domain;

/// <summary>A staff login for one tenant. Authorization comes from the roles' permission bundles, never from the role name.</summary>
public class StaffUser : TenantEntity
{
    private readonly List<UserRole> _roles = [];
    private StaffUser() { }

    public string UserName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public bool MustChangePassword { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedOutUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public IReadOnlyList<UserRole> Roles => _roles;

    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static StaffUser Create(Guid id, Guid tenantId, string userName, string email, string displayName, string? phone, string passwordHash, Guid createdBy, DateTimeOffset now, bool mustChangePassword)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length < 3)
            throw new DomainRuleException("identity.user.username_invalid", "User name must be at least 3 characters.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainRuleException("identity.user.display_name_required", "Display name is required.");
        return new StaffUser
        {
            Id = id, TenantId = tenantId,
            UserName = userName.Trim().ToLowerInvariant(),
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            PhoneNumber = phone,
            PasswordHash = passwordHash,
            IsActive = true,
            MustChangePassword = mustChangePassword,
            CreatedAt = now,
            CreatedByUserId = createdBy,
        };
    }

    public bool IsLockedOut(DateTimeOffset now) => LockedOutUntil is { } until && until > now;

    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedAttempts)
        {
            LockedOutUntil = now + LockoutDuration;
            FailedLoginAttempts = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
        LastLoginAt = now;
    }

    public void SetPasswordHash(string hash, bool mustChange) { PasswordHash = hash; MustChangePassword = mustChange; }
    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
    public void UpdateProfile(string email, string displayName, string? phone) { Email = email.Trim().ToLowerInvariant(); DisplayName = displayName.Trim(); PhoneNumber = phone; }

    /// <summary>Replaces role assignments. Returns the newly added rows so a service can register them as Added on a tracked user.</summary>
    public IReadOnlyList<UserRole> SetRoles(IEnumerable<Guid> roleIds)
    {
        var target = roleIds.Distinct().ToHashSet();
        _roles.RemoveAll(r => !target.Contains(r.RoleId));
        var added = target.Where(id => _roles.All(r => r.RoleId != id)).Select(id => new UserRole(Id, id)).ToList();
        _roles.AddRange(added);
        return added;
    }
}

public class UserRole
{
    private UserRole() { }
    internal UserRole(Guid userId, Guid roleId) { UserId = userId; RoleId = roleId; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}
