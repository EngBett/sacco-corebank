namespace Sacco.Shared.Notifications;

/// <summary>
/// In-app notifications for staff. Modules raise them at the points where someone else has to act
/// (a journal awaiting a checker, a loan awaiting the committee) or where the initiator should learn the
/// outcome (approved, rejected, paid). Implemented by the Notifications module, which persists each
/// notification per recipient and pushes it over SignalR to any connected portal session.
/// Never a substitute for the audit trail: the audit log is the record, this is the nudge.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(NotificationRequest request, CancellationToken ct);
}

/// <param name="Kind">Dotted machine kind, e.g. <c>ledger.journal.pending</c>. Drives the icon and grouping in the portal.</param>
/// <param name="Title">Short headline, e.g. "Journal MJ-2026-0007 awaits approval".</param>
/// <param name="Body">One sentence of context.</param>
/// <param name="Link">Portal-relative path to act on the notification, e.g. <c>/ledger/journals/{id}</c>.</param>
/// <param name="Audience">Who receives it.</param>
/// <param name="ActorUserId">Who caused it. Excluded from permission-based fan-out — nobody needs to be told what they just did.</param>
public sealed record NotificationRequest(
    string Kind,
    string Title,
    string Body,
    string? Link,
    NotificationAudience Audience,
    Guid ActorUserId);

/// <summary>Either an explicit set of users or "everyone in the tenant who holds a permission".</summary>
public sealed record NotificationAudience
{
    private NotificationAudience(IReadOnlyList<Guid> userIds, string? permission)
    {
        UserIds = userIds;
        Permission = permission;
    }

    public IReadOnlyList<Guid> UserIds { get; }
    public string? Permission { get; }

    public static NotificationAudience Users(params Guid[] userIds) => new(userIds, null);
    public static NotificationAudience User(Guid userId) => new([userId], null);
    public static NotificationAudience HoldersOf(string permission) => new([], permission);
}
