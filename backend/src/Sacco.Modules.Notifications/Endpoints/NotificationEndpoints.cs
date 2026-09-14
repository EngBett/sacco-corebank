using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sacco.Modules.Notifications.Application;
using Sacco.Modules.Notifications.Realtime;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Notifications.Endpoints;

public sealed record UnreadCountResponse(int Unread);
public sealed record MarkAllReadResponse(int Marked);
/// <param name="HubPath">Path of the SignalR hub on the API host; the client appends the ticket as <c>access_token</c>.</param>
public sealed record HubTicketResponse(string Ticket, DateTimeOffset ExpiresAt, string HubPath);

/// <summary>
/// Every endpoint is scoped to the caller's own notifications, so authentication is the only requirement:
/// there is no permission to "view someone's notifications" because nobody may.
/// </summary>
public sealed class NotificationEndpoints(string corsPolicy) : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        g.MapGet("", async (NotificationService svc, ICurrentUser user, bool unreadOnly = false, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
            TypedResults.Ok(await svc.ListAsync(user.UserId, unreadOnly, page, pageSize, ct)))
            .WithName("ListNotifications");

        g.MapGet("/unread-count", async (NotificationService svc, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(new UnreadCountResponse(await svc.UnreadCountAsync(user.UserId, ct))))
            .WithName("GetUnreadNotificationCount");

        g.MapPost("/{id:guid}/read", async (Guid id, NotificationService svc, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(await svc.MarkReadAsync(id, user.UserId, ct)))
            .WithName("MarkNotificationRead");

        g.MapPost("/read-all", async (NotificationService svc, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(new MarkAllReadResponse(await svc.MarkAllReadAsync(user.UserId, ct))))
            .WithName("MarkAllNotificationsRead");

        // Exchanges the caller's (BFF-held) API token for a short-lived ticket the browser may hold.
        g.MapPost("/hub-ticket", async (IHubTicketIssuer issuer, ICurrentUser user, ITenantContext tenant, CancellationToken ct) =>
        {
            var ticket = await issuer.IssueAsync(user.UserId, user.UserName, tenant.TenantId, tenant.TenantSlug, ct);
            return TypedResults.Ok(new HubTicketResponse(ticket.Token, ticket.ExpiresAt, NotificationsHub.Path));
        }).WithName("IssueNotificationHubTicket");

        app.MapHub<NotificationsHub>(NotificationsHub.Path).RequireCors(corsPolicy);
    }
}
