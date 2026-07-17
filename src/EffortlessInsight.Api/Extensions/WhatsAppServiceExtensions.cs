using EffortlessInsight.Api.HealthChecks;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.WhatsApp;
using EffortlessInsight.Api.Services.WhatsApp.Commands;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Polly.Extensions.Http;

namespace EffortlessInsight.Api.Extensions;

/// <summary>
/// Extension methods for registering WhatsApp services.
/// </summary>
public static class WhatsAppServiceExtensions
{
    /// <summary>
    /// Add WhatsApp bot services to the DI container.
    /// </summary>
    public static IServiceCollection AddWhatsAppServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options
        services.Configure<MetaWhatsAppOptions>(
            configuration.GetSection(MetaWhatsAppOptions.SectionName));

        // Register Meta WhatsApp client with retry policy
        services.AddHttpClient<IMetaWhatsAppClient, MetaWhatsAppClient>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        // Register core services
        services.AddScoped<IWhatsAppSessionService, WhatsAppSessionService>();
        services.AddScoped<IWhatsAppVerificationService, WhatsAppVerificationService>();
        services.AddScoped<IWhatsAppMessageLogService, WhatsAppMessageLogService>();
        services.AddScoped<IWhatsAppTemplateService, WhatsAppTemplateService>();
        services.AddScoped<IWhatsAppBotService, WhatsAppBotService>();
        services.AddScoped<IWhatsAppWebhookIdempotencyService, WhatsAppWebhookIdempotencyService>();

        // Register command router
        services.AddScoped<CommandRouter>();

        // Register command handlers
        services.AddScoped<ICommandHandler, StartCommandHandler>();
        services.AddScoped<ICommandHandler, LinkCommandHandler>();
        services.AddScoped<ICommandHandler, HelpCommandHandler>();
        services.AddScoped<ICommandHandler, StatusCommandHandler>();
        services.AddScoped<ICommandHandler, NoticesCommandHandler>();
        services.AddScoped<ICommandHandler, DeadlinesCommandHandler>();
        services.AddScoped<ICommandHandler, TasksCommandHandler>();
        services.AddScoped<ICommandHandler, StopCommandHandler>();

        // Register background jobs
        services.AddScoped<WhatsAppJobs>();

        return services;
    }

    /// <summary>
    /// Configure WhatsApp options validation.
    /// </summary>
    public static IServiceCollection AddWhatsAppOptionsValidation(
        this IServiceCollection services)
    {
        services.AddOptions<MetaWhatsAppOptions>()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Add WhatsApp health check.
    /// </summary>
    public static IHealthChecksBuilder AddWhatsAppHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "whatsapp",
        string[]? tags = null)
    {
        return builder.AddCheck<WhatsAppHealthCheck>(
            name,
            tags: tags ?? ["ready"]);
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }
}
