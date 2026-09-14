namespace Sacco.Modules.Notifications.Application;

public sealed record NotificationResponse(Guid Id, string Kind, string Title, string Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>Delivers an already-persisted notification to live sessions. The SignalR implementation is registered by the API host; tools (seed) get the no-op.</summary>
public interface INotificationPusher
{
    Task PushAsync(Guid tenantId, IReadOnlyList<Guid> recipientUserIds, NotificationResponse notification, CancellationToken ct);
}

public sealed class NoOpNotificationPusher : INotificationPusher
{
    public Task PushAsync(Guid tenantId, IReadOnlyList<Guid> recipientUserIds, NotificationResponse notification, CancellationToken ct) => Task.CompletedTask;
}
