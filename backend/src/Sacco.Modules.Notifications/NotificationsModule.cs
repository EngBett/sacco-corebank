using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sacco.Modules.Notifications.Channels;
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
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<NotificationsDbContext>(connectionString, NotificationsDbContext.SchemaName);
        services.Configure<NotificationChannelSettings>(configuration.GetSection(NotificationChannelSettings.SectionName));
        services.AddScoped<OutboundDispatcher>();
        // Out-of-band channels: sandbox needs no credentials; Live is a configuration switch (Notifications:Sms:Mode / Notifications:Email:Mode).
        // SMS is additionally provider-agnostic: Notifications:Sms:Provider picks the gateway behind ISmsSender (see SmsProviderNames).
        // Email needs no such switch — SmtpEmailSender speaks plain SMTP, so any provider (Mailpit locally, SES/SendGrid/a SACCO's own
        // mail server in production) is just a Notifications:Email:Smtp:Host/Port change.
        if (string.Equals(configuration["Notifications:Sms:Mode"], "Live", StringComparison.OrdinalIgnoreCase))
            RegisterLiveSmsSender(services, configuration["Notifications:Sms:Provider"] ?? SmsProviderNames.AfricasTalking);
        else services.AddSingleton<ISmsSender, SandboxSmsSender>();
        if (string.Equals(configuration["Notifications:Email:Mode"], "Live", StringComparison.OrdinalIgnoreCase)) services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else services.AddSingleton<IEmailSender, SandboxEmailSender>();
        services.AddScoped<NotificationService>();
        services.AddScoped<INotifier>(sp => sp.GetRequiredService<NotificationService>());
        services.TryAddSingleton<INotificationPusher, NoOpNotificationPusher>();
        return services;
    }

    /// <summary>
    /// Registers exactly one concrete <see cref="ISmsSender"/> for the named gateway. Adding a new gateway is: implement
    /// <see cref="ISmsSender"/> in <c>Channels/SmsProviders.cs</c>, add its settings block to <see cref="SmsSettings"/>,
    /// add one case here — every caller of <see cref="ISmsSender"/> (starting with <see cref="Application.OutboundDispatcher"/>)
    /// needs no change.
    /// </summary>
    private static void RegisterLiveSmsSender(IServiceCollection services, string provider)
    {
        switch (provider)
        {
            case var p when string.Equals(p, SmsProviderNames.AfricasTalking, StringComparison.OrdinalIgnoreCase):
                services.AddHttpClient<AfricasTalkingSmsSender>();
                services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<AfricasTalkingSmsSender>());
                break;
            case var p when string.Equals(p, SmsProviderNames.Twilio, StringComparison.OrdinalIgnoreCase):
                services.AddHttpClient<TwilioSmsSender>();
                services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<TwilioSmsSender>());
                break;
            case var p when string.Equals(p, SmsProviderNames.WhatsApp, StringComparison.OrdinalIgnoreCase):
                services.AddHttpClient<WhatsAppCloudApiSmsSender>();
                services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<WhatsAppCloudApiSmsSender>());
                break;
            case var p when string.Equals(p, SmsProviderNames.Safaricom, StringComparison.OrdinalIgnoreCase):
                services.AddHttpClient<SafaricomSmsSender>();
                services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<SafaricomSmsSender>());
                break;
            case var p when string.Equals(p, SmsProviderNames.Airtel, StringComparison.OrdinalIgnoreCase):
                services.AddHttpClient<AirtelSmsSender>();
                services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<AirtelSmsSender>());
                break;
            case var p when string.Equals(p, SmsProviderNames.Mock, StringComparison.OrdinalIgnoreCase):
                services.AddHttpClient<MockSmsSender>();
                services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<MockSmsSender>());
                break;
            default:
                throw new InvalidOperationException($"Unknown Notifications:Sms:Provider '{provider}'. Supported: {SmsProviderNames.AfricasTalking}, {SmsProviderNames.Twilio}, {SmsProviderNames.WhatsApp}, {SmsProviderNames.Safaricom}, {SmsProviderNames.Airtel}, {SmsProviderNames.Mock}.");
        }
    }

    /// <summary>SignalR hub, REST endpoints and the live pusher.</summary>
    /// <param name="corsPolicy">Policy that allows the portal origin with credentials; browsers negotiate the hub cross-origin.</param>
    /// <param name="redisConnection">When set, a Redis backplane fans pushes out across API replicas.</param>
    public static IServiceCollection AddNotificationsRealtime(this IServiceCollection services, string corsPolicy, string? redisConnection = null)
    {
        var signalR = services.AddSignalR();
        if (!string.IsNullOrWhiteSpace(redisConnection))
            signalR.AddStackExchangeRedis(redisConnection, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("sacco-notifications"));
        services.Replace(ServiceDescriptor.Singleton<INotificationPusher, SignalRNotificationPusher>());
        services.AddSingleton<IModuleEndpoints>(new NotificationEndpoints(corsPolicy));
        return services;
    }
}
