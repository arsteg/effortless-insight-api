using EffortlessInsight.Api.Hubs;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Services.Notifications;
using Resend;

namespace EffortlessInsight.Api.Extensions;

/// <summary>
/// Extension methods for registering notification services
/// </summary>
public static class NotificationServiceExtensions
{
    /// <summary>
    /// Add notification services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddNotificationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.Configure<FirebaseOptions>(configuration.GetSection(FirebaseOptions.SectionName));

        // Register Resend client
        services.AddOptions();
        services.AddHttpClient<ResendClient>();
        services.Configure<ResendClientOptions>(o =>
        {
            o.ApiToken = configuration.GetSection("Resend:ApiKey").Value ?? string.Empty;
        });
        services.AddTransient<IResend, ResendClient>();

        // Core notification services
        services.AddScoped<INotificationEngineService, NotificationEngineService>();
        services.AddScoped<INotificationPreferencesService, NotificationPreferencesService>();
        services.AddScoped<INotificationTemplateService, NotificationTemplateService>();
        services.AddScoped<ITemplateRenderService, TemplateRenderService>();
        services.AddScoped<IDeliveryTrackingService, DeliveryTrackingService>();
        services.AddScoped<IPushTokenService, PushTokenService>();
        services.AddScoped<IChannelUnsubscribeService, ChannelUnsubscribeService>();
        services.AddScoped<IDeadLetterService, DeadLetterService>();

        // Channel services
        services.AddScoped<IEmailChannelService, ResendEmailService>();
        services.AddScoped<ISmsChannelService, DisabledSmsService>();
        services.AddScoped<IPushChannelService, FirebasePushService>();
        services.AddScoped<IWhatsAppChannelService, MetaWhatsAppChannelService>();
        services.AddScoped<IInAppChannelService, InAppNotificationService>();

        // SignalR connection manager (singleton for shared state)
        // Note: With Redis backplane, connection state is managed by SignalR itself
        services.AddSingleton<IConnectionManager, InMemoryConnectionManager>();

        // Route Clients.User(...) by the JWT "sub" claim so real-time delivery
        // works across the Redis backplane (audit BE-11).
        services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, SignalRUserIdProvider>();

        // Background jobs
        services.AddScoped<NotificationJobs>();

        // Get Redis connection string for SignalR backplane
        var redisConnectionString = configuration.GetConnectionString("Redis");

        // SignalR hub with Redis backplane for multi-server support (GAP-NOTIF-003)
        var signalRBuilder = services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        });

        // Add Redis backplane if Redis connection is configured
        // This enables SignalR to work across multiple server instances
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
            {
                options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("EffortlessInsight:SignalR:");
            });
        }

        return services;
    }

    /// <summary>
    /// Map notification endpoints (SignalR hub)
    /// </summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<NotificationHub>("/hubs/notifications").RequireAuthorization();
        return endpoints;
    }

    /// <summary>
    /// Map AI chat endpoints (SignalR hub)
    /// </summary>
    public static IEndpointRouteBuilder MapAIChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<Hubs.ChatHub>("/hubs/chat");
        return endpoints;
    }
}
