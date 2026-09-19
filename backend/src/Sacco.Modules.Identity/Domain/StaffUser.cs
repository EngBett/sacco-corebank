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

    /// <summary>The office this user works at (ADR 0018); stamped on their postings and audit entries.</summary>
    public Guid? BranchId { get; private set; }
    public IReadOnlyList<UserRole> Roles => _roles;

    /// <summary>When the user set their first password. Null for an invited user who hasn't used their activation link yet.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>Authenticator (TOTP) secret, encrypted by <c>TotpSecretProtector</c>. Null until enrolment is confirmed.</summary>
    public string? TotpSecret { get; private set; }

    /// <summary>Secret shown on the set-up page but not yet confirmed with a code, so a page reload shows the same QR code.</summary>
    public string? TotpPendingSecret { get; private set; }
    public DateTimeOffset? TotpEnabledAt { get; private set; }

    /// <summary>Last accepted TOTP time step: a code can't be replayed within its 30-second window.</summary>
    public long? TotpLastUsedStep { get; private set; }

    public bool IsActivated => ActivatedAt is not null;
    public bool IsMfaEnabled => TotpEnabledAt is not null && TotpSecret is not null;

    public StaffUserStatus Status => !IsActive ? StaffUserStatus.Deactivated : IsActivated ? StaffUserStatus.Active : StaffUserStatus.Invited;

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
            // A user created with a password (seed data, tests) is active straight away; invitations use CreateInvited.
            ActivatedAt = string.IsNullOrEmpty(passwordHash) ? null : now,
        };
    }

    /// <summary>An approved invitation: the account exists (so the user name is taken) but can't sign in until activated.</summary>
    public static StaffUser CreateInvited(Guid id, Guid tenantId, string userName, string email, string displayName, string? phone, Guid invitedBy, DateTimeOffset now)
        => Create(id, tenantId, userName, email, displayName, phone, passwordHash: string.Empty, invitedBy, now, mustChangePassword: false);

    public void Activate(string passwordHash, DateTimeOffset now)
    {
        if (IsActivated) throw new DomainRuleException("identity.user.already_activated", "This account has already been activated.");
        if (!IsActive) throw new DomainRuleException("identity.user.inactive", "This account has been deactivated.");
        PasswordHash = passwordHash;
        MustChangePassword = false;
        ActivatedAt = now;
    }

    /// <summary>Returns the pending secret, creating it with <paramref name="create"/> the first time.</summary>
    public string EnsurePendingTotpSecret(Func<string> create)
    {
        if (IsMfaEnabled) throw new DomainRuleException("identity.mfa.already_enabled", "Two-step verification is already set up.");
        return TotpPendingSecret ??= create();
    }

    public void EnableTotp(DateTimeOffset now, long confirmedStep)
    {
        if (TotpPendingSecret is null) throw new DomainRuleException("identity.mfa.not_started", "Two-step verification set-up hasn't been started.");
        TotpSecret = TotpPendingSecret;
        TotpPendingSecret = null;
        TotpEnabledAt = now;
        TotpLastUsedStep = confirmedStep;
    }

    /// <summary>Accepts a TOTP time step only if it is newer than the last one used (replay protection).</summary>
    public bool TryUseTotpStep(long step)
    {
        if (TotpLastUsedStep is { } last && step <= last) return false;
        TotpLastUsedStep = step;
        return true;
    }

    /// <summary>Clears two-step verification; the user must enrol again at their next sign-in.</summary>
    public void ResetMfa()
    {
        TotpSecret = null;
        TotpPendingSecret = null;
        TotpEnabledAt = null;
        TotpLastUsedStep = null;
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

    public void SetBranch(Guid? branchId) => BranchId = branchId;

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

public enum StaffUserStatus { Invited, Active, Deactivated }

public class UserRole
{
    private UserRole() { }
    internal UserRole(Guid userId, Guid roleId) { UserId = userId; RoleId = roleId; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}
