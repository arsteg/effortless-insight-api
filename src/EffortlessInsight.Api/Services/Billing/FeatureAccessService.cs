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

        var features = subscription.Plan.Features ?? [];

        // Cache the features
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
            _logger.LogWarning(ex, "Failed to cache features for organization {OrganizationId}", organizationId);
        }

        return features;
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
