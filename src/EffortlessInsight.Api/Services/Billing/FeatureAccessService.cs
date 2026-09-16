using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for checking feature access based on subscription plan.
/// </summary>
public class FeatureAccessService : IFeatureAccessService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<FeatureAccessService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public FeatureAccessService(
        ApplicationDbContext dbContext,
        IDistributedCache cache,
        ILogger<FeatureAccessService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> HasFeatureAccessAsync(
        Guid organizationId,
        string featureCode,
        CancellationToken cancellationToken = default)
    {
        // The CA operator plan used to short-circuit to full access here, ignoring
        // subscription status entirely. It is no longer special-cased: a CA is granted the
        // plan as a normal active subscription, so revoking it (which cancels the
        // subscription) must actually remove access. The plan's own unlimited feature list
        // is what grants a CA everything while the subscription is active.
        var features = await GetAvailableFeaturesAsync(organizationId, cancellationToken);
        return features.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<string>> GetAvailableFeaturesAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cacheKey = $"org_features:{organizationId}";
        var cachedFeatures = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (!string.IsNullOrEmpty(cachedFeatures))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(cachedFeatures) ?? [];
            }
            catch
            {
                // Cache corrupted, continue to fetch from DB
            }
        }

        // Get organization's subscription and plan features
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .Where(s => s.OrganizationId == organizationId && s.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription?.Plan == null)
        {
            _logger.LogWarning("No active subscription found for organization {OrganizationId}", organizationId);
            return [];
        }

        // Check if subscription is active (not cancelled or expired)
        if (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired)
        {
            _logger.LogInformation(
                "Organization {OrganizationId} has {Status} subscription, no features available",
                organizationId,
                subscription.Status);
            return [];
        }

        // SECURITY FIX: For "trialing" status, verify the plan actually has a trial period
        // and that the trial hasn't expired. This prevents access when:
        // - User selected a paid plan but cancelled/abandoned payment (subscription stuck in "trialing")
        // - Plan has no trial period (TrialDays == 0)
        if (subscription.Status == SubscriptionStatus.Trialing)
        {
            var plan = subscription.Plan;

            // If plan has no trial period, subscription should not be in trialing state with features
            // This happens when user cancels payment before completing checkout
            if (plan.TrialDays <= 0)
            {
                _logger.LogWarning(
                    "Organization {OrganizationId} has trialing subscription but plan {PlanCode} has no trial period (TrialDays={TrialDays}). " +
                    "Denying feature access - payment was likely never completed.",
                    organizationId, plan.Code, plan.TrialDays);
                return [];
            }

            // If plan has trial but TrialEnd is not set or has passed, deny access
            if (!subscription.TrialEnd.HasValue || subscription.TrialEnd.Value < DateTime.UtcNow)
            {
                _logger.LogInformation(
                    "Organization {OrganizationId} trial has expired or is not set (TrialEnd={TrialEnd}). No features available.",
                    organizationId, subscription.TrialEnd);
                return [];
            }

            // Valid trial - TrialEnd is in the future, allow access
            _logger.LogDebug(
                "Organization {OrganizationId} is in valid trial period until {TrialEnd}",
                organizationId, subscription.TrialEnd.Value);
        }

        var features = subscription.Plan.Features ?? [];

        await CacheFeaturesAsync(cacheKey, features, cancellationToken);

        return features;
    }

    private async Task CacheFeaturesAsync(
        string cacheKey,
        List<string> features,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(features),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache features for {CacheKey}", cacheKey);
        }
    }

    public async Task RequireFeatureAccessAsync(
        Guid organizationId,
        string featureCode,
        CancellationToken cancellationToken = default)
    {
        var hasAccess = await HasFeatureAccessAsync(organizationId, featureCode, cancellationToken);

        if (!hasAccess)
        {
            _logger.LogWarning(
                "Organization {OrganizationId} attempted to access feature '{FeatureCode}' but it's not available in their plan",
                organizationId,
                featureCode);

            // Try to find which plan includes this feature
            var requiredPlan = await GetLowestPlanWithFeatureAsync(featureCode, cancellationToken);

            throw new FeatureNotAvailableException(featureCode, requiredPlan);
        }
    }

    /// <summary>
    /// Invalidates the feature cache for an organization (call after plan change).
    /// </summary>
    public async Task InvalidateCacheAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"org_features:{organizationId}";
        await _cache.RemoveAsync(cacheKey, cancellationToken);
    }

    private async Task<string?> GetLowestPlanWithFeatureAsync(string featureCode, CancellationToken cancellationToken)
    {
        // Find the lowest-tier plan that has this feature
        var plan = await _dbContext.SubscriptionPlans
            .Where(p => p.IsActive && p.DeletedAt == null)
            .Where(p => p.Features.Contains(featureCode))
            .OrderBy(p => p.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        return plan?.DisplayName;
    }
}
