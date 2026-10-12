using EffortlessInsight.Api.Options;

namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Extension methods for registering email sync services.
/// </summary>
public static class EmailSyncServiceExtensions
{
    /// <summary>
    /// Adds email sync services to the service collection.
    /// </summary>
    public static IServiceCollection AddEmailSyncServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register options
        services.AddOptions<EmailSyncOptions>().Bind(configuration.GetSection(EmailSyncOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        // Register provider clients
        services.AddHttpClient("EmailInspection", client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddHttpClient<GmailProviderClient>(client => { client.Timeout = TimeSpan.FromSeconds(45); client.MaxResponseContentBufferSize = 36 * 1024 * 1024; });
        services.AddHttpClient<OutlookProviderClient>(client => { client.Timeout = TimeSpan.FromSeconds(45); client.MaxResponseContentBufferSize = 36 * 1024 * 1024; });

        services.AddScoped<IEmailOAuthAttemptStore, EmailOAuthAttemptStore>();
        services.AddScoped<IEmailOperationLock, EmailOperationLock>();
        services.AddScoped<IEmailNoticeInspector, EmailNoticeInspector>();
        services.AddScoped<IEmailProviderClient>(sp => sp.GetRequiredService<GmailProviderClient>());
        services.AddScoped<IEmailProviderClient>(sp => sp.GetRequiredService<OutlookProviderClient>());

        // Register services
        services.AddScoped<INoticeDetectionService, NoticeDetectionService>();
        services.AddScoped<IEmailSyncService, EmailSyncService>();

        return services;
    }
}
