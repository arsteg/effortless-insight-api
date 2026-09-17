using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Ca;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

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

        // CA onboarding, and the invitation endpoints a Business Owner must be able to
        // reach before they have any subscription of their own.
        "/api/v1/ca/register",
        "/api/v1/ca/profile",
        "/api/v1/ca/organization",
        "/api/v1/ca/invitations/validate",
        "/api/v1/ca/invitations/accept",
        "/api/v1/ca/invitations/decline",
        "/api/v1/ca/invitations/pending",

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
        // "/api/v1/ca/" used to be exempt wholesale, from when CAs had a separate portal
        // and no organization of their own. They now work inside their clients'
        // organizations through the ordinary endpoints, so a blanket exemption would just
        // mean a suspended CA keeps their client-management screens. The specific paths
        // needed before entitlement exists are listed in ExactPublicPaths instead.
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
        ApplicationDbContext dbContext,
        ISubscriptionStatusEvaluator evaluator,
        ICaActingContextService caActingContext)
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

        var decision = await evaluator.EvaluateAsync(orgId.Value, context.RequestAborted);
        if (decision.Allowed)
        {
            await _next(context);
            return;
        }

        // Denial path only, so ordinary requests pay for none of what follows.

        // A CA works inside their clients' organizations but pays through their own firm.
        // While that entitlement is live, the client organization's subscription state is
        // not their problem. ResolveAsync returns immediately for anyone who is not a CA
        // acting for a client, so enforcement for Business Owners is unchanged.
        var acting = await caActingContext.ResolveAsync(context.RequestAborted);
        if (acting != null &&
            await evaluator.HasLiveCaEntitlementAsync(acting.CaBillingOrganizationId, context.RequestAborted))
        {
            await _next(context);
            return;
        }

        // A CA whose free access no administrator has decided on yet is waiting for
        // approval, not refusing to pay, so tell the client to show "awaiting approval"
        // rather than the plan-selection flow. Access is denied either way.
        var errorCode = decision.ErrorCode;
        var message = decision.Message;
        if (decision.StatusCode == StatusCodes.Status402PaymentRequired &&
            await IsAwaitingCaApprovalAsync(context, dbContext))
        {
            errorCode = "CA_APPROVAL_PENDING";
            message = "Your CA account is awaiting approval. You'll get access once an administrator approves it.";
        }

        var body = new Dictionary<string, object?>
        {
            ["success"] = false,
            ["error"] = errorCode,
            ["message"] = message
        };

        if (decision.Detail != null)
        {
            foreach (var (key, value) in decision.Detail)
            {
                body[key] = value;
            }
        }

        context.Response.StatusCode = decision.StatusCode;
        await context.Response.WriteAsJsonAsync(body);
    }

    /// <summary>
    /// True when the caller is a CA whose free access no administrator has decided on yet
    /// (never granted, never revoked). Used only to pick the error code on the denial path.
    /// </summary>
    private static async Task<bool> IsAwaitingCaApprovalAsync(
        HttpContext context,
        ApplicationDbContext dbContext)
    {
        if (context.User.FindFirst("is_ca")?.Value != "true")
        {
            return false;
        }

        var userIdClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return false;
        }

        return await dbContext.CaProfiles
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId && !p.AllowFreePlan && p.FreePlanRevokedAt == null);
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
