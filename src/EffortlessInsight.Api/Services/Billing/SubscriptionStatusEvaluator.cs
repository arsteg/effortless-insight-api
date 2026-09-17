using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Ca;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Whether an organization's subscription currently permits access, and if not, why.
/// </summary>
/// <param name="Allowed">True when the organization may be used.</param>
/// <param name="StatusCode">HTTP status to return on denial.</param>
/// <param name="ErrorCode">Machine-readable code the web client branches on.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Detail">Extra fields to merge into the denial body, if any.</param>
public sealed record SubscriptionDecision(
    bool Allowed,
    int StatusCode = StatusCodes.Status200OK,
    string? ErrorCode = null,
    string? Message = null,
    IReadOnlyDictionary<string, object?>? Detail = null)
{
    public static readonly SubscriptionDecision Allow = new(true);
}

public interface ISubscriptionStatusEvaluator
{
    /// <summary>
    /// Evaluates whether <paramref name="organizationId"/> may currently be used.
    /// </summary>
    Task<SubscriptionDecision> EvaluateAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Whether a CA's own firm carries a live entitlement, which under the CA distribution
    /// model covers work in any of their linked clients' organizations.
    /// </summary>
    Task<bool> HasLiveCaEntitlementAsync(Guid caBillingOrganizationId, CancellationToken ct = default);
}

/// <summary>
/// Extracted from SubscriptionEnforcementMiddleware so that the client-organization check
/// and the acting-CA entitlement check cannot drift apart.
/// </summary>
public class SubscriptionStatusEvaluator : ISubscriptionStatusEvaluator
{
    private static readonly string[] ValidStatuses = ["trial", "trialing", "active", "past_due"];

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<SubscriptionStatusEvaluator> _logger;

    public SubscriptionStatusEvaluator(
        ApplicationDbContext dbContext,
        ILogger<SubscriptionStatusEvaluator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<SubscriptionDecision> EvaluateAsync(Guid organizationId, CancellationToken ct = default)
    {
        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        if (org == null)
        {
            _logger.LogWarning("Organization {OrganizationId} not found", organizationId);
            return new SubscriptionDecision(
                false,
                StatusCodes.Status404NotFound,
                "ORGANIZATION_NOT_FOUND",
                "Organization not found");
        }

        // SECURITY FIX #1: Block paused subscriptions immediately
        if (org.SubscriptionStatus == "paused")
        {
            _logger.LogWarning(
                "Access denied: Subscription paused for organization {OrganizationId}", organizationId);

            return new SubscriptionDecision(
                false,
                StatusCodes.Status402PaymentRequired,
                "SUBSCRIPTION_PAUSED",
                "Your subscription is paused. Please resume your subscription to continue using the application.",
                new Dictionary<string, object?> { ["subscriptionStatus"] = org.SubscriptionStatus });
        }

        // SECURITY FIX #2: Check grace period expiration for past_due subscriptions
        if (org.SubscriptionStatus == "past_due")
        {
            var subscription = await _dbContext.BillingSubscriptions
                .AsNoTracking()
                .Where(s => s.OrganizationId == organizationId && s.DeletedAt == null)
                .FirstOrDefaultAsync(ct);

            if (subscription?.GracePeriodEndAt != null && subscription.GracePeriodEndAt <= DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "Access denied: Grace period expired for organization {OrganizationId}. Grace period ended at {GracePeriodEnd}",
                    organizationId, subscription.GracePeriodEndAt);

                return new SubscriptionDecision(
                    false,
                    StatusCodes.Status402PaymentRequired,
                    "GRACE_PERIOD_EXPIRED",
                    "Your grace period has expired. Please update your payment method to continue using the application.",
                    new Dictionary<string, object?>
                    {
                        ["subscriptionStatus"] = org.SubscriptionStatus,
                        ["gracePeriodEndedAt"] = subscription.GracePeriodEndAt
                    });
            }
        }

        // SECURITY FIX #3: Check CurrentPeriodEnd as fallback for active subscriptions
        if (org.SubscriptionStatus == "active")
        {
            var subscription = await _dbContext.BillingSubscriptions
                .AsNoTracking()
                .Where(s => s.OrganizationId == organizationId && s.DeletedAt == null)
                .FirstOrDefaultAsync(ct);

            if (subscription?.CurrentPeriodEnd != null && subscription.CurrentPeriodEnd < DateTime.UtcNow)
            {
                // Only check for non-Razorpay-managed subscriptions or those without auto-renewal.
                // Razorpay-managed subscriptions will be updated via webhook.
                if (string.IsNullOrEmpty(subscription.RazorpaySubscriptionId) || subscription.CancelAtPeriodEnd)
                {
                    _logger.LogWarning(
                        "Access denied: Subscription period ended for organization {OrganizationId}. Period ended at {CurrentPeriodEnd}",
                        organizationId, subscription.CurrentPeriodEnd);

                    return new SubscriptionDecision(
                        false,
                        StatusCodes.Status402PaymentRequired,
                        "SUBSCRIPTION_PERIOD_ENDED",
                        "Your subscription period has ended. Please renew your subscription to continue.",
                        new Dictionary<string, object?>
                        {
                            ["subscriptionStatus"] = org.SubscriptionStatus,
                            ["periodEndedAt"] = subscription.CurrentPeriodEnd
                        });
                }
            }
        }

        if (!ValidStatuses.Contains(org.SubscriptionStatus))
        {
            _logger.LogInformation(
                "Access denied for organization {OrganizationId} with subscription status: {Status}",
                organizationId, org.SubscriptionStatus);

            return new SubscriptionDecision(
                false,
                StatusCodes.Status402PaymentRequired,
                "SUBSCRIPTION_REQUIRED",
                "An active subscription is required to access this resource. Please select a plan or renew your subscription.",
                new Dictionary<string, object?> { ["subscriptionStatus"] = org.SubscriptionStatus });
        }

        // Check if trial has expired (support both "trial" and "trialing" for backwards compatibility)
        if ((org.SubscriptionStatus == "trial" || org.SubscriptionStatus == "trialing") && org.TrialEndsAt.HasValue)
        {
            if (org.TrialEndsAt.Value < DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "Trial expired for organization {OrganizationId}. Expired on: {TrialEnd}",
                    organizationId, org.TrialEndsAt.Value);

                return new SubscriptionDecision(
                    false,
                    StatusCodes.Status402PaymentRequired,
                    "TRIAL_EXPIRED",
                    "Your free trial has expired. Please subscribe to a plan to continue using the application.",
                    new Dictionary<string, object?>
                    {
                        ["subscriptionStatus"] = org.SubscriptionStatus,
                        ["trialEndedAt"] = org.TrialEndsAt.Value
                    });
            }
        }

        return SubscriptionDecision.Allow;
    }

    public async Task<bool> HasLiveCaEntitlementAsync(
        Guid caBillingOrganizationId,
        CancellationToken ct = default)
    {
        // Normally the ca_operator plan is granted as a real active subscription, so the
        // ordinary evaluation carries it.
        var decision = await EvaluateAsync(caBillingOrganizationId, ct);
        if (decision.Allowed)
        {
            return true;
        }

        // CaProfileService tolerates the plan grant succeeding while activating the
        // subscription fails. An administrator who granted free access meant it, so honour
        // the flag itself rather than stranding the CA on a billing accident.
        return await _dbContext.CaProfiles
            .AsNoTracking()
            .AnyAsync(p =>
                p.User.OrganizationId == caBillingOrganizationId &&
                p.Status == CaProfileStatus.Active &&
                p.AllowFreePlan &&
                p.FreePlanRevokedAt == null,
                ct);
    }
}
