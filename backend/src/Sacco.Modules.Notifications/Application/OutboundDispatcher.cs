using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sacco.Modules.Notifications.Channels;
using Sacco.Modules.Notifications.Domain;
using Sacco.Modules.Notifications.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Notifications;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Notifications.Application;

public sealed record OutboundMessageResponse(Guid Id, Guid? NotificationId, Guid RecipientUserId, MessageChannel Channel, string Address, string? Subject, string Body, OutboundStatus Status, string? ProviderReference, string? Error, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);
public sealed record ChannelInfo(string Sms, bool SmsSandbox, string Email, bool EmailSandbox, IReadOnlyList<string> SmsKindPrefixes, bool EmailEnabled);

/// <summary>
/// Turns a persisted notification into SMS/email rows and sends them. Failures are recorded on the row,
/// never thrown at the workflow that raised the notification — an SMS gateway outage must not stop a loan approval.
/// </summary>
public sealed class OutboundDispatcher(NotificationsDbContext db, IUserDirectory users, ISmsSender sms, IEmailSender email, IOptions<NotificationChannelSettings> options, ITenantContext tenant, IClock clock, ILogger<OutboundDispatcher> logger)
{
    private DeliverySettings Delivery => options.Value.Delivery;

    public ChannelInfo Info => new(sms.Name, sms.IsSandbox, email.Name, email.IsSandbox, Delivery.SmsKindPrefixes, Delivery.EmailEnabled);

    public async Task DispatchAsync(Notification n, CancellationToken ct)
    {
        UserContact? contact;
        try { contact = await users.GetContactAsync(tenant.TenantId, n.RecipientUserId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Contact lookup failed for {User}", n.RecipientUserId); return; }
        if (contact is null) return;

        var rows = new List<OutboundMessage>();
        if (Delivery.EmailEnabled && !string.IsNullOrWhiteSpace(contact.Email))
            rows.Add(OutboundMessage.Create(tenant.TenantId, n.Id, n.RecipientUserId, MessageChannel.Email, contact.Email, n.Title, $"{n.Body}\n\n{(n.Link is null ? "" : $"Open: {n.Link}\n")}— {tenant.TenantSlug} SACCO portal", clock.UtcNow));
        if (!string.IsNullOrWhiteSpace(contact.PhoneNumber) && Delivery.SmsKindPrefixes.Any(p => n.Kind.StartsWith(p, StringComparison.Ordinal)))
            rows.Add(OutboundMessage.Create(tenant.TenantId, n.Id, n.RecipientUserId, MessageChannel.Sms, contact.PhoneNumber, null, Truncate($"{n.Title}. {n.Body}", 300), clock.UtcNow));
        if (rows.Count == 0) return;

        db.OutboundMessages.AddRange(rows);
        await db.SaveChangesAsync(ct);
        foreach (var row in rows) await SendAsync(row, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SendAsync(OutboundMessage row, CancellationToken ct)
    {
        try
        {
            var reference = row.Channel == MessageChannel.Sms
                ? await sms.SendAsync(row.Address, row.Body, ct)
                // The stored row.Body is plain text (shown verbatim in the admin outbox); wrap it into HTML only at the SMTP boundary.
                : await email.SendAsync(row.Address, row.Subject ?? "Notification", System.Net.WebUtility.HtmlEncode(row.Body).Replace("\n", "<br/>"), null, ct);
            row.MarkSent(reference, clock.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Channel} delivery to {Address} failed", row.Channel, row.Address);
            row.MarkFailed(ex.Message);
        }
    }

    public async Task<PagedResult<OutboundMessageResponse>> ListAsync(OutboundStatus? status, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.OutboundMessages.AsNoTracking();
        if (status is { } s) q = q.Where(m => m.Status == s);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<OutboundMessageResponse>(items.Select(ToResponse).ToList(), page, pageSize, total);
    }

    public async Task<OutboundMessageResponse> RetryAsync(Guid id, CancellationToken ct)
    {
        var row = await db.OutboundMessages.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("Outbound message", id);
        if (row.Status == OutboundStatus.Sent) throw new DomainRuleException("notifications.outbox.already_sent", "This message was already delivered.");
        await SendAsync(row, ct);
        await db.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public static OutboundMessageResponse ToResponse(OutboundMessage m) => new(m.Id, m.NotificationId, m.RecipientUserId, m.Channel, m.Address, m.Subject, m.Body, m.Status, m.ProviderReference, m.Error, m.Attempts, m.CreatedAt, m.SentAt);
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
