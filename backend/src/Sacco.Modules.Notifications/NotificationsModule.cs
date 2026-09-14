using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sacco.Modules.Notifications.Application;
using Sacco.Modules.Notifications.Endpoints;
using Sacco.Modules.Notifications.Persistence;
using Sacco.Modules.Notifications.Realtime;
using Sacco.Shared.Http;
using Sacco.Shared.Notifications;
using Sacco.Shared.Persistence;

namespace Sacco.Modules.Notifications;

public static class NotificationsModule
{
    /// <summary>Persistence and the <see cref="INotifier"/> contract. Live delivery is a no-op until <see cref="AddNotificationsRealtime"/> is called (the API host does; the seed tool does not).</summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<NotificationsDbContext>(connectionString, NotificationsDbContext.SchemaName);
        services.AddScoped<NotificationService>();
        services.AddScoped<INotifier>(sp => sp.GetRequiredService<NotificationService>());
        services.TryAddSingleton<INotificationPusher, NoOpNotificationPusher>();
        return services;
    }

    /// <summary>SignalR hub, REST endpoints and the live pusher.</summary>
    /// <param name="corsPolicy">Policy that allows the portal origin with credentials; browsers negotiate the hub cross-origin.</param>
    public static IServiceCollection AddNotificationsRealtime(this IServiceCollection services, string corsPolicy)
    {
        services.AddSignalR();
        services.Replace(ServiceDescriptor.Singleton<INotificationPusher, SignalRNotificationPusher>());
        services.AddSingleton<IModuleEndpoints>(new NotificationEndpoints(corsPolicy));
        return services;
    }
}
