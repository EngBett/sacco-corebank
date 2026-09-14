using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sacco.Modules.Notifications.Application;
using Sacco.Shared.Auth;

namespace Sacco.Modules.Notifications.Realtime;

/// <summary>
/// Server-to-client only. Clients authenticate with a hub ticket (never the API bearer token) and are
/// placed in a per-(tenant, user) group; the pusher addresses that group. There are no client-callable
/// methods: reads and acknowledgements go through the REST endpoints so they are authorised and audited
/// like everything else.
/// </summary>
[Authorize(AuthenticationSchemes = HubTicketAuth.Scheme)]
public sealed class NotificationsHub : Hub
{
    public const string Path = HubTicketAuth.PathPrefix + "/notifications";
    /// <summary>Client method invoked with a <see cref="NotificationResponse"/> payload.</summary>
    public const string NotificationMethod = "notification";

    public static string GroupFor(Guid tenantId, Guid userId) => $"tenant:{tenantId:N}:user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User ?? throw new HubException("Unauthenticated.");
        var sub = user.FindFirstValue("sub");
        var tenant = user.FindFirstValue("tenant_id");
        if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(tenant, out var tenantId))
            throw new HubException("Hub ticket is missing its subject or tenant.");
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(tenantId, userId));
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRNotificationPusher(IHubContext<NotificationsHub> hub) : INotificationPusher
{
    public async Task PushAsync(Guid tenantId, IReadOnlyList<Guid> recipientUserIds, NotificationResponse notification, CancellationToken ct)
    {
        foreach (var userId in recipientUserIds)
            await hub.Clients.Group(NotificationsHub.GroupFor(tenantId, userId)).SendAsync(NotificationsHub.NotificationMethod, notification, ct);
    }
}
