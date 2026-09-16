using EffortlessInsight.Api.Services.Ca;

namespace EffortlessInsight.Api.Extensions;

/// <summary>
/// Extension methods for registering CA (Chartered Accountant) services.
/// </summary>
public static class CaServiceExtensions
{
    /// <summary>
    /// Adds CA distribution channel services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddCaServices(this IServiceCollection services)
    {
        // CA Profile management
        services.AddScoped<ICaProfileService, CaProfileService>();

        // CA Invitation management (bidirectional: CA invites BO, BO invites CA)
        services.AddScoped<ICaInvitationService, CaInvitationService>();

        // CA Client relationship management
        services.AddScoped<ICaClientService, CaClientService>();

        // CA Authorization (GSTIN-level permission checks)
        services.AddScoped<ICaAuthorizationService, CaAuthorizationService>();

        // CA Context management (client context switching, JWT refresh)
        services.AddScoped<ICaContextService, CaContextService>();

        // CA Notice access (with authorization filtering)
        services.AddScoped<ICaNoticeService, CaNoticeService>();

        // CA Dashboard aggregation
        services.AddScoped<ICaDashboardService, CaDashboardService>();

        // CA Notice staging (pre-claim notice management)
        services.AddScoped<ICaStagingService, CaStagingService>();

        return services;
    }
}
