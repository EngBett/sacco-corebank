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

/// <summary>
/// Outbound email, usable by any module (not just the Notifications module's own per-notification dispatch).
/// Implemented by the Notifications module (sandbox by default, SMTP via MailKit when
/// <c>Notifications:Email:Mode=Live</c> — see ADR 0012); a module that needs to email something — a report,
/// a statement — depends on this Shared contract, never on Notifications' internal sender types.
/// </summary>
public interface IEmailSender
{
    string Name { get; }
    bool IsSandbox { get; }
    Task<string?> SendAsync(string to, string subject, string bodyHtml, IReadOnlyList<EmailAttachment>? attachments, CancellationToken ct);
}

/// <param name="ContentType">MIME type, e.g. <c>application/pdf</c>.</param>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

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
