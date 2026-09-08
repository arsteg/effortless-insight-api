using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
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

    // Paths that don't require a subscription - use exact matching for security
    private static readonly HashSet<string> ExactPublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // Auth endpoints - always allowed
        "/api/v1/auth/login",
        "/api/v1/auth/register",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/reset-password",
        "/api/v1/auth/verify-email",
        "/api/v1/auth/resend-verification",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",
        "/api/v1/auth/me",
        "/api/v1/auth/oauth",

        // Billing/subscription endpoints - specific paths only (not broad prefix)
        "/api/v1/plans",
        "/api/v1/subscriptions",  // POST to create new subscription
        "/api/v1/subscriptions/trial",
        "/api/v1/subscriptions/create",
        "/api/v1/subscriptions/verify",
        "/api/v1/subscriptions/verify-subscription",
        "/api/v1/subscriptions/current",
        "/api/v1/subscriptions/current/resume",  // Allow paused users to resume
        "/api/v1/subscriptions/current/pause",   // Allow active users to pause
        "/api/v1/subscriptions/current/reactivate",  // Allow cancelled users to reactivate
        "/api/v1/coupons/validate",
        "/api/v1/invoices",
        "/api/v1/payment-methods",

        // Organization list/create endpoints - needed during onboarding
        "/api/v1/organizations",
        "/api/v1/organizations/current",

        // SignalR hubs - needed for real-time notifications regardless of subscription status
        "/hubs/notifications",
        "/hubs/notices",
        "/hubs/chat"
    };

    // Prefixes that are safe to allow (truly public endpoints)
    private static readonly string[] SafePrefixes = new[]
    {
        "/health",
        "/metrics",
        "/hangfire",
        "/hubs/",
        "/api/v1/organizations/validate-gstin/",  // GSTIN validation during onboarding
        // In-app support must work regardless of subscription state — a customer
        // blocked by a payment/limit problem is exactly who needs to raise a ticket.
        // Endpoints are still [Authorize]-protected and org-scoped.
        "/api/v1/support/"
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

        // SECURITY FIX #9: Block requests without organization selection
        var orgId = currentOrganization.OrganizationId;
        if (orgId == null)
        {
            _logger.LogWarning("User authenticated but no organization selected - blocking access");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "NO_ORGANIZATION_SELECTED",
                message = "Please select an organization to continue"
            });
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

        // SECURITY FIX #1: Block paused subscriptions immediately
        if (org.SubscriptionStatus == "paused")
        {
            _logger.LogWarning(
                "Access denied: Subscription paused for organization {OrganizationId}",
                orgId.Value);

            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "SUBSCRIPTION_PAUSED",
                message = "Your subscription is paused. Please resume your subscription to continue using the application.",
                subscriptionStatus = org.SubscriptionStatus
            });
            return;
        }

        // SECURITY FIX #2: Check grace period expiration for past_due subscriptions
        if (org.SubscriptionStatus == "past_due")
        {
            var subscription = await dbContext.BillingSubscriptions
                .AsNoTracking()
                .Where(s => s.OrganizationId == orgId.Value && s.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (subscription?.GracePeriodEndAt != null && subscription.GracePeriodEndAt <= DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Access denied: Grace period expired for organization {OrganizationId}. Grace period ended at {GracePeriodEnd}",
                    orgId.Value, subscription.GracePeriodEndAt);

                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error = "GRACE_PERIOD_EXPIRED",
                    message = "Your grace period has expired. Please update your payment method to continue using the application.",
                    subscriptionStatus = org.SubscriptionStatus,
                    gracePeriodEndedAt = subscription.GracePeriodEndAt
                });
                return;
            }
        }

        // SECURITY FIX #3: Check CurrentPeriodEnd as fallback for active subscriptions
        if (org.SubscriptionStatus == "active")
        {
            var subscription = await dbContext.BillingSubscriptions
                .AsNoTracking()
                .Where(s => s.OrganizationId == orgId.Value && s.DeletedAt == null)
                .FirstOrDefaultAsync();

            if (subscription?.CurrentPeriodEnd != null && subscription.CurrentPeriodEnd < DateTime.UtcNow)
            {
                // Only check for non-Razorpay-managed subscriptions or those without auto-renewal
                // Razorpay-managed subscriptions will be updated via webhook
                if (string.IsNullOrEmpty(subscription.RazorpaySubscriptionId) || subscription.CancelAtPeriodEnd)
                {
                    _logger.LogWarning(
                        "Access denied: Subscription period ended for organization {OrganizationId}. Period ended at {CurrentPeriodEnd}",
                        orgId.Value, subscription.CurrentPeriodEnd);

                    context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        error = "SUBSCRIPTION_PERIOD_ENDED",
                        message = "Your subscription period has ended. Please renew your subscription to continue.",
                        subscriptionStatus = org.SubscriptionStatus,
                        periodEndedAt = subscription.CurrentPeriodEnd
                    });
                    return;
                }
            }
        }

        // Check subscription status - allow trial (or trialing), active, past_due (within grace period)
        var validStatuses = new[] { "trial", "trialing", "active", "past_due" };
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

        // Check if trial has expired (support both "trial" and "trialing" for backwards compatibility)
        if ((org.SubscriptionStatus == "trial" || org.SubscriptionStatus == "trialing") && org.TrialEndsAt.HasValue)
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

    // SECURITY FIX #8: Use exact path matching for sensitive endpoints
    private static bool IsPublicPath(string path)
    {
        // Exact match for most public paths
        if (ExactPublicPaths.Contains(path))
            return true;

        // Only allow prefix matching for truly safe prefixes (health, metrics, hubs)
        // This prevents path manipulation attacks like /api/v1/subscriptions/../refund
        foreach (var prefix in SafePrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
