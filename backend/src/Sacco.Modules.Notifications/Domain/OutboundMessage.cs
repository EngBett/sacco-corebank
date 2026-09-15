using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Notifications.Domain;

public enum MessageChannel { Sms = 1, Email = 2 }
public enum OutboundStatus { Queued = 1, Sent = 2, Failed = 3 }

/// <summary>
/// One SMS or email derived from a notification. Kept as a row so delivery is auditable and retryable, and
/// so the sandbox can be demonstrated without a gateway: the outbox *is* the evidence.
/// </summary>
public class OutboundMessage : TenantEntity
{
    private OutboundMessage() { }

    public Guid? NotificationId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public MessageChannel Channel { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string? Subject { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public OutboundStatus Status { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? Error { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public static OutboundMessage Create(Guid tenantId, Guid? notificationId, Guid recipientUserId, MessageChannel channel, string address, string? subject, string body, DateTimeOffset now)
        => new() { Id = Ids.New(), TenantId = tenantId, NotificationId = notificationId, RecipientUserId = recipientUserId, Channel = channel, Address = address, Subject = subject, Body = body, Status = OutboundStatus.Queued, CreatedAt = now };

    public void MarkSent(string? reference, DateTimeOffset now) { Attempts++; Status = OutboundStatus.Sent; ProviderReference = reference; Error = null; SentAt = now; }
    public void MarkFailed(string error) { Attempts++; Status = OutboundStatus.Failed; Error = error.Length > 500 ? error[..500] : error; }
}
