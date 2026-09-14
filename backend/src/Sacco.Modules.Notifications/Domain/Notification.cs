using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Notifications.Domain;

/// <summary>One notification for one recipient. Read state is per recipient by construction.</summary>
public class Notification : TenantEntity
{
    private Notification() { }

    public Guid RecipientUserId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? Link { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public static Notification Create(Guid tenantId, Guid recipientUserId, Guid actorUserId, string kind, string title, string body, string? link, DateTimeOffset now)
    {
        if (recipientUserId == Guid.Empty) throw new DomainRuleException("notifications.recipient_required", "A notification needs a recipient.");
        if (string.IsNullOrWhiteSpace(kind)) throw new DomainRuleException("notifications.kind_required", "A notification needs a kind.");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainRuleException("notifications.title_required", "A notification needs a title.");
        if (link is not null && !link.StartsWith('/')) throw new DomainRuleException("notifications.link_relative", "Notification links must be portal-relative paths.");
        return new Notification
        {
            Id = Ids.New(),
            TenantId = tenantId,
            RecipientUserId = recipientUserId,
            ActorUserId = actorUserId,
            Kind = kind.Trim(),
            Title = title.Trim(),
            Body = body?.Trim() ?? string.Empty,
            Link = link,
            CreatedAt = now,
        };
    }

    /// <summary>Idempotent: the first read time is kept.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
