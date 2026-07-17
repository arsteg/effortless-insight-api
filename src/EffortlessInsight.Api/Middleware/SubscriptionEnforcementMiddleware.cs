using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Middleware to enforce subscription requirements for API access.
/// Blocks requests from organizations without an active subscription or trial.
/// </summary>
public class SubscriptionEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SubscriptionEnforcementMiddleware> _logger;

    // Paths that don't require a subscription
    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/auth/login",
        "/api/v1/auth/register",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/reset-password",
        "/api/v1/auth/verify-email",
        "/api/v1/auth/resend-verification",
        "/api/v1/auth/refresh",
        "/api/v1/plans",
        "/api/v1/plans/compare",
        "/api/v1/subscriptions/trial",
        "/api/v1/subscriptions",
        "/api/v1/subscriptions/verify",
        "/api/v1/organizations",
        "/health",
        "/metrics",
        "/hangfire"
    };

    public SubscriptionEnforcementMiddleware(
        RequestDelegate next,
        ILogger<SubscriptionEnforcementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentOrganizationService currentOrganization,
        ApplicationDbContext dbContext)
    {
        // Skip if not authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        // Skip if admin authentication
        if (context.User.HasClaim(c => c.Type == "admin_id"))
        {
            await _next(context);
            return;
        }

        // Skip public paths
        var path = context.Request.Path.Value ?? string.Empty;
        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        // Check subscription status
        var orgId = currentOrganization.OrganizationId;
        if (orgId == null)
        {
            _logger.LogWarning("User authenticated but no organization selected");
            await _next(context);
            return;
        }

        var org = await dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orgId.Value);

        if (org == null)
        {
            _logger.LogWarning("Organization {OrganizationId} not found", orgId.Value);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "ORGANIZATION_NOT_FOUND",
                message = "Organization not found"
            });
            return;
        }

        // Check subscription status
        var validStatuses = new[] { "trial", "active", "past_due" };
        if (!validStatuses.Contains(org.SubscriptionStatus))
        {
            _logger.LogInformation(
                "Access denied for organization {OrganizationId} with subscription status: {Status}",
                orgId.Value, org.SubscriptionStatus);

            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "SUBSCRIPTION_REQUIRED",
                message = "An active subscription is required to access this resource. Please select a plan or renew your subscription.",
                subscriptionStatus = org.SubscriptionStatus
            });
            return;
        }

        // Check if trial has expired
        if (org.SubscriptionStatus == "trial" && org.TrialEndsAt.HasValue)
        {
            if (org.TrialEndsAt.Value < DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "Trial expired for organization {OrganizationId}. Expired on: {TrialEnd}",
                    orgId.Value, org.TrialEndsAt.Value);

                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "TRIAL_EXPIRED",
                    message = "Your free trial has expired. Please subscribe to a plan to continue using the application.",
                    trialEndedAt = org.TrialEndsAt.Value,
                    subscriptionStatus = org.SubscriptionStatus
                });
                return;
            }
        }

        await _next(context);
    }

    private static bool IsPublicPath(string path)
    {
        // Exact match
        if (PublicPaths.Contains(path))
            return true;

        // Check if path starts with any public path
        foreach (var publicPath in PublicPaths)
        {
            if (path.StartsWith(publicPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Extension methods for subscription enforcement middleware.
/// </summary>
public static class SubscriptionEnforcementMiddlewareExtensions
{
    /// <summary>
    /// Adds subscription enforcement middleware to the pipeline.
    /// Should be added after authentication but before MVC.
    /// </summary>
    public static IApplicationBuilder UseSubscriptionEnforcement(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SubscriptionEnforcementMiddleware>();
    }
}
