using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Notifications.Domain;
using Sacco.Modules.Notifications.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Notifications;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Notifications.Application;

/// <summary>
/// Persists one row per recipient, then pushes to connected sessions. Persisting first means a
/// notification is never lost because nobody was online; pushing after commit means a live session
/// never sees something the database will not confirm on the next page load.
/// </summary>
public sealed class NotificationService(NotificationsDbContext db, IUserDirectory users, INotificationPusher pusher, OutboundDispatcher outbound, ITenantContext tenant, IClock clock, ILogger<NotificationService> logger) : INotifier
{
    public async Task NotifyAsync(NotificationRequest request, CancellationToken ct)
    {
        var recipients = await ResolveRecipientsAsync(request, ct);
        if (recipients.Count == 0)
        {
            logger.LogDebug("Notification {Kind} had no recipients (audience {Permission})", request.Kind, request.Audience.Permission);
            return;
        }

        var now = clock.UtcNow;
        var rows = recipients.Select(r => Notification.Create(tenant.TenantId, r, request.ActorUserId, request.Kind, request.Title, request.Body, request.Link, now)).ToList();
        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync(ct);

        // Every recipient's row has the same content; the id differs per recipient, so push each one so the client can mark it read.
        foreach (var row in rows)
        {
            await pusher.PushAsync(tenant.TenantId, [row.RecipientUserId], ToResponse(row), ct);
            await outbound.DispatchAsync(row, ct); // SMS/email; failures land on the outbox row, never here
        }
    }

    private async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(NotificationRequest request, CancellationToken ct)
    {
        var set = new HashSet<Guid>(request.Audience.UserIds.Where(id => id != Guid.Empty && id != SystemActors.System));
        if (request.Audience.Permission is { } permission)
        {
            foreach (var id in await users.UsersWithPermissionAsync(tenant.TenantId, permission, ct))
                if (id != request.ActorUserId) set.Add(id);
        }
        return set.ToList();
    }

    public async Task<PagedResult<NotificationResponse>> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<NotificationResponse>(items.Select(ToResponse).ToList(), page, pageSize, total);
    }

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken ct)
        => db.Notifications.AsNoTracking().CountAsync(n => n.RecipientUserId == userId && n.ReadAt == null, ct);

    /// <summary>Marks one of the caller's notifications read. Another user's notification is "not found", never "forbidden" — ids must not leak.</summary>
    public async Task<NotificationResponse> MarkReadAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == userId, ct)
            ?? throw new NotFoundException("Notification", id);
        n.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToResponse(n);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.Notifications.Where(n => n.RecipientUserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    public static NotificationResponse ToResponse(Notification n) => new(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt);
}
