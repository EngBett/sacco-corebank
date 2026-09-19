using System.Net.Mail;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Domain;

public enum StaffInvitationStatus { PendingApproval, Sent, Accepted, Rejected, Revoked }

/// <summary>
/// A proposal to give someone a portal account (ADR 0016). Proposed by a holder of <c>admin.users.invite</c> or
/// <c>admin.users.manage</c>; a proposal from someone without <c>admin.users.manage</c> needs a different user who has it to
/// approve (maker-checker). Approval creates the <see cref="StaffUser"/> (not yet activated) and sends the activation link.
/// </summary>
public class StaffInvitation : TenantEntity
{
    private StaffInvitation() { }

    public string UserName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public List<Guid> RoleIds { get; private set; } = [];

    /// <summary>Office the new user will work at (ADR 0018).</summary>
    public Guid? BranchId { get; private set; }
    public StaffInvitationStatus Status { get; private set; }
    public Guid ProposedByUserId { get; private set; }
    public DateTimeOffset ProposedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    /// <summary>The account created on approval.</summary>
    public Guid? UserId { get; private set; }
    public DateTimeOffset? LastSentAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    public static StaffInvitation Propose(Guid id, Guid tenantId, string userName, string email, string displayName, string? phone,
        IReadOnlyCollection<Guid> roleIds, Guid proposedBy, DateTimeOffset now, Guid? branchId = null)
    {
        userName = userName.Trim().ToLowerInvariant();
        if (userName.Length < 3 || userName.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new DomainRuleException("identity.invitation.username_invalid", "User name must be at least 3 characters: letters, digits, '.', '_' or '-'.");
        if (!MailAddress.TryCreate(email.Trim(), out var address) || address.Address != email.Trim())
            throw new DomainRuleException("identity.invitation.email_invalid", "Enter a valid email address — the activation link is sent there.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainRuleException("identity.invitation.display_name_required", "Display name is required.");
        if (roleIds.Count == 0)
            throw new DomainRuleException("identity.invitation.roles_required", "Choose at least one role for the new user.");
        return new StaffInvitation
        {
            Id = id,
            TenantId = tenantId,
            UserName = userName,
            Email = address.Address.ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            RoleIds = roleIds.Distinct().ToList(),
            BranchId = branchId,
            Status = StaffInvitationStatus.PendingApproval,
            ProposedByUserId = proposedBy,
            ProposedAt = now,
        };
    }

    /// <summary>
    /// Records the approval. <paramref name="selfApproved"/> is true only when the proposer holds <c>admin.users.manage</c>
    /// themselves; otherwise the approver must be a different person.
    /// </summary>
    public void Approve(Guid approvedBy, DateTimeOffset now, bool selfApproved)
    {
        EnsureStatus(StaffInvitationStatus.PendingApproval, "approved");
        if (!selfApproved) MakerChecker.EnsureDistinct(ProposedByUserId, approvedBy, "Approve staff invitation");
        DecidedByUserId = approvedBy;
        DecidedAt = now;
    }

    public void MarkSent(Guid userId, DateTimeOffset now)
    {
        if (Status is not (StaffInvitationStatus.PendingApproval or StaffInvitationStatus.Sent) || DecidedByUserId is null)
            throw new DomainRuleException("identity.invitation.not_approved", "Only an approved invitation can be sent.");
        UserId = userId;
        Status = StaffInvitationStatus.Sent;
        LastSentAt = now;
    }

    public void Reject(Guid rejectedBy, string? reason, DateTimeOffset now)
    {
        EnsureStatus(StaffInvitationStatus.PendingApproval, "rejected");
        MakerChecker.EnsureDistinct(ProposedByUserId, rejectedBy, "Reject staff invitation");
        Status = StaffInvitationStatus.Rejected;
        DecidedByUserId = rejectedBy;
        DecidedAt = now;
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Revoke(Guid revokedBy, DateTimeOffset now)
    {
        if (Status is not (StaffInvitationStatus.PendingApproval or StaffInvitationStatus.Sent))
            throw new DomainRuleException("identity.invitation.not_revocable", $"A {Status} invitation can't be revoked.");
        Status = StaffInvitationStatus.Revoked;
        DecidedByUserId ??= revokedBy;
        DecidedAt ??= now;
    }

    public void MarkAccepted(DateTimeOffset now)
    {
        EnsureStatus(StaffInvitationStatus.Sent, "accepted");
        Status = StaffInvitationStatus.Accepted;
        AcceptedAt = now;
    }

    private void EnsureStatus(StaffInvitationStatus expected, string action)
    {
        if (Status != expected)
            throw new DomainRuleException("identity.invitation.wrong_status", $"A {Status} invitation can't be {action}.");
    }
}
