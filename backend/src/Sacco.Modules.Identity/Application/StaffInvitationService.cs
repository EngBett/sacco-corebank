using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Notifications;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Application;

public sealed record ProposeInvitationCommand(string UserName, string Email, string DisplayName, string? PhoneNumber, IReadOnlyList<Guid> RoleIds, Guid? BranchId = null);

/// <summary>
/// New portal users (ADR 0016). A user administrator (<c>admin.users.manage</c>) invites directly; anyone else with
/// <c>admin.users.invite</c> (a Branch Manager) proposes, and a user administrator approves before the email goes out.
/// </summary>
public sealed class StaffInvitationService(
    IdentityDbContext db,
    TenantContext tenant,
    StaffAccountService accounts,
    IPermissionResolver permissions,
    IBranchDirectory branches,
    INotifier notifier,
    IClock clock,
    IAuditLogger audit)
{
    public Task<List<StaffInvitation>> ListAsync(StaffInvitationStatus? status, CancellationToken ct) =>
        db.StaffInvitations.AsNoTracking()
            .Where(i => status == null || i.Status == status)
            .OrderByDescending(i => i.ProposedAt)
            .ToListAsync(ct);

    public async Task<StaffInvitation> ProposeAsync(ProposeInvitationCommand cmd, Guid byUser, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (cmd.BranchId is { } branch) await branches.EnsureExistsAsync(branch, ct);
        var invitation = StaffInvitation.Propose(Ids.New(), tenant.TenantId, cmd.UserName, cmd.Email, cmd.DisplayName, cmd.PhoneNumber, cmd.RoleIds, byUser, now, cmd.BranchId);
        await EnsureRolesExistAsync(invitation.RoleIds, ct);
        await EnsureUserNameFreeAsync(invitation.UserName, invitation.Id, ct);
        db.StaffInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.invitation.proposed", nameof(StaffInvitation), invitation.Id.ToString(), byUser,
            $$"""{"userName":"{{invitation.UserName}}","roles":[{{string.Join(",", invitation.RoleIds.Select(r => $"\"{r}\""))}}]}"""), ct);

        if (await CanManageUsersAsync(byUser, ct))
            return await ApproveInternalAsync(invitation, byUser, selfApproved: true, ct);

        await notifier.NotifyAsync(new NotificationRequest("identity.invitation.pending", $"New staff user awaits approval: {invitation.DisplayName}",
            $"Proposed user name '{invitation.UserName}' ({invitation.Email}).", "/admin/users", NotificationAudience.HoldersOf(Permissions.Admin.UsersManage), byUser), ct);
        return invitation;
    }

    public async Task<StaffInvitation> ApproveAsync(Guid invitationId, Guid byUser, CancellationToken ct)
    {
        var invitation = await FindAsync(invitationId, ct);
        return await ApproveInternalAsync(invitation, byUser, selfApproved: false, ct);
    }

    public async Task<StaffInvitation> RejectAsync(Guid invitationId, string? reason, Guid byUser, CancellationToken ct)
    {
        var invitation = await FindAsync(invitationId, ct);
        invitation.Reject(byUser, reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.invitation.rejected", nameof(StaffInvitation), invitation.Id.ToString(), byUser), ct);
        await notifier.NotifyAsync(new NotificationRequest("identity.invitation.rejected", $"Staff invitation declined: {invitation.DisplayName}",
            invitation.RejectionReason ?? "No reason given.", "/admin/users", NotificationAudience.User(invitation.ProposedByUserId), byUser), ct);
        return invitation;
    }

    /// <summary>Sends a fresh activation link (the previous one stops working).</summary>
    public async Task<StaffInvitation> ResendAsync(Guid invitationId, Guid byUser, CancellationToken ct)
    {
        var invitation = await FindAsync(invitationId, ct);
        if (invitation.Status != StaffInvitationStatus.Sent || invitation.UserId is null)
            throw new DomainRuleException("identity.invitation.not_resendable", $"A {invitation.Status} invitation can't be resent.");
        var user = await db.Users.FirstAsync(u => u.Id == invitation.UserId, ct);
        await accounts.SendActivationAsync(user, byUser, ct);
        invitation.MarkSent(user.Id, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.invitation.resent", nameof(StaffInvitation), invitation.Id.ToString(), byUser), ct);
        return invitation;
    }

    /// <summary>Withdraws a pending or unaccepted invitation; an account created for it is deactivated and its link stops working.</summary>
    public async Task<StaffInvitation> RevokeAsync(Guid invitationId, Guid byUser, CancellationToken ct)
    {
        var invitation = await FindAsync(invitationId, ct);
        var now = clock.UtcNow;
        invitation.Revoke(byUser, now);
        if (invitation.UserId is { } userId)
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            if (!user.IsActivated) user.Deactivate();
            foreach (var token in await db.StaffAccountTokens.Where(t => t.UserId == userId && t.UsedAt == null).ToListAsync(ct))
                token.Consume(now);
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("identity.invitation.revoked", nameof(StaffInvitation), invitation.Id.ToString(), byUser), ct);
        return invitation;
    }

    private async Task<StaffInvitation> ApproveInternalAsync(StaffInvitation invitation, Guid byUser, bool selfApproved, CancellationToken ct)
    {
        var now = clock.UtcNow;
        invitation.Approve(byUser, now, selfApproved);
        await EnsureRolesExistAsync(invitation.RoleIds, ct);
        await EnsureUserNameFreeAsync(invitation.UserName, invitation.Id, ct);

        var user = StaffUser.CreateInvited(Ids.New(), tenant.TenantId, invitation.UserName, invitation.Email, invitation.DisplayName, invitation.PhoneNumber, invitation.ProposedByUserId, now);
        user.SetBranch(invitation.BranchId);
        user.SetRoles(invitation.RoleIds);
        db.Users.Add(user);
        invitation.MarkSent(user.Id, now);
        await db.SaveChangesAsync(ct);
        await accounts.SendActivationAsync(user, byUser, ct);

        await audit.RecordAsync(new AuditEvent("identity.invitation.approved", nameof(StaffInvitation), invitation.Id.ToString(), byUser,
            $$"""{"userId":"{{user.Id}}","selfApproved":{{(selfApproved ? "true" : "false")}}}"""), ct);
        if (!selfApproved)
            await notifier.NotifyAsync(new NotificationRequest("identity.invitation.approved", $"Staff invitation approved: {invitation.DisplayName}",
                $"An activation link was emailed to {invitation.Email}.", "/admin/users", NotificationAudience.User(invitation.ProposedByUserId), byUser), ct);
        return invitation;
    }

    private async Task<StaffInvitation> FindAsync(Guid id, CancellationToken ct) =>
        await db.StaffInvitations.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Staff invitation", id);

    private async Task<bool> CanManageUsersAsync(Guid userId, CancellationToken ct) =>
        (await permissions.GetPermissionsAsync(tenant.TenantId, userId, ct)).Contains(Permissions.Admin.UsersManage);

    private async Task EnsureRolesExistAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct)
    {
        var found = await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct);
        if (found != roleIds.Distinct().Count()) throw new NotFoundException("Role", string.Join(",", roleIds));
    }

    private async Task EnsureUserNameFreeAsync(string userName, Guid invitationId, CancellationToken ct)
    {
        var taken = await db.Users.AnyAsync(u => u.UserName == userName, ct)
            || await db.StaffInvitations.AnyAsync(i => i.Id != invitationId && i.UserName == userName && i.Status == StaffInvitationStatus.PendingApproval, ct);
        if (taken) throw new ConflictException("identity.user.duplicate", $"The user name '{userName}' is already taken.");
    }
}
