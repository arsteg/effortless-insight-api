using EffortlessInsight.Api.Constants;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Result of GSTIN limit validation.
/// </summary>
public record GstinLimitValidationResult(
    bool IsAllowed,
    string? ErrorCode,
    string? ErrorMessage,
    int CurrentCount,
    int Limit,
    bool IsUnlimited);

/// <summary>
/// Result of subscription status validation.
/// </summary>
public record SubscriptionValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage,
    string? Status);

/// <summary>
/// DTO for GSTIN limit status.
/// </summary>
public record GstinLimitStatusDto(
    int CurrentCount,
    int Limit,
    bool IsUnlimited,
    bool CanAddMore);

/// <summary>
/// Interface for GSTIN limit enforcement service.
/// </summary>
public interface IGstinLimitEnforcementService
{
    /// <summary>
    /// Validates if an organization can add a new GSTIN based on subscription limits.
    /// </summary>
    Task<GstinLimitValidationResult> ValidateCanAddGstinAsync(Guid orgId, string? gstin = null);

    /// <summary>
    /// Validates subscription status for GSTIN operations.
    /// </summary>
    Task<SubscriptionValidationResult> ValidateSubscriptionStatusAsync(Guid orgId);

    /// <summary>
    /// Gets the effective GSTIN count for an organization.
    /// For regular orgs: count of OrganizationGstins.
    /// For CA operators: count of distinct client GSTINs (staging + active).
    /// </summary>
    Task<int> GetEffectiveGstinCountAsync(Guid orgId);

    /// <summary>
    /// Validates that a GSTIN belongs to an organization.
    /// </summary>
    Task<bool> ValidateGstinBelongsToOrgAsync(Guid orgId, string gstin);

    /// <summary>
    /// Gets the current GSTIN limit status for an organization.
    /// </summary>
    Task<GstinLimitStatusDto> GetLimitStatusAsync(Guid orgId);

    /// <summary>
    /// Acquires a distributed lock for GSTIN addition operations to prevent race conditions.
    /// </summary>
    Task<IAsyncDisposable?> AcquireGstinAdditionLockAsync(Guid orgId, TimeSpan timeout);
}

/// <summary>
/// Service for enforcing GSTIN limits based on subscription plans.
/// </summary>
public class GstinLimitEnforcementService : IGstinLimitEnforcementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<GstinLimitEnforcementService> _logger;

    private const string LockKeyPrefix = "gstin_limit_lock:";
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    public GstinLimitEnforcementService(
        ApplicationDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<GstinLimitEnforcementService> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    public async Task<GstinLimitValidationResult> ValidateCanAddGstinAsync(Guid orgId, string? gstin = null)
    {
        // First validate subscription status
        var subscriptionResult = await ValidateSubscriptionStatusAsync(orgId);
        if (!subscriptionResult.IsValid)
        {
            return new GstinLimitValidationResult(
                IsAllowed: false,
                ErrorCode: subscriptionResult.ErrorCode,
                ErrorMessage: subscriptionResult.ErrorMessage,
                CurrentCount: 0,
                Limit: 0,
                IsUnlimited: false);
        }

        // Get plan limits
        var planLimits = await GetPlanLimitsAsync(orgId);
        if (planLimits == null)
        {
            return new GstinLimitValidationResult(
                IsAllowed: false,
                ErrorCode: ErrorCodes.SubscriptionRequired,
                ErrorMessage: "No active subscription found. Please subscribe to a plan.",
                CurrentCount: 0,
                Limit: 0,
                IsUnlimited: false);
        }

        var limit = planLimits.GstinsAllowed;

        // -1 means unlimited
        if (limit == -1)
        {
            return new GstinLimitValidationResult(
                IsAllowed: true,
                ErrorCode: null,
                ErrorMessage: null,
                CurrentCount: 0,
                Limit: -1,
                IsUnlimited: true);
        }

        // Get current GSTIN count
        var currentCount = await GetEffectiveGstinCountAsync(orgId);

        if (currentCount >= limit)
        {
            return new GstinLimitValidationResult(
                IsAllowed: false,
                ErrorCode: ErrorCodes.GstinLimitExceeded,
                ErrorMessage: $"GSTIN limit of {limit} reached. You have {currentCount} GSTIN(s). Please upgrade your plan to add more GSTINs.",
                CurrentCount: currentCount,
                Limit: limit,
                IsUnlimited: false);
        }

        return new GstinLimitValidationResult(
            IsAllowed: true,
            ErrorCode: null,
            ErrorMessage: null,
            CurrentCount: currentCount,
            Limit: limit,
            IsUnlimited: false);
    }

    public async Task<SubscriptionValidationResult> ValidateSubscriptionStatusAsync(Guid orgId)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.DeletedAt == null);

        if (subscription == null)
        {
            return new SubscriptionValidationResult(
                IsValid: false,
                ErrorCode: ErrorCodes.SubscriptionRequired,
                ErrorMessage: "No subscription found. Please subscribe to a plan.",
                Status: null);
        }

        var now = DateTime.UtcNow;

        switch (subscription.Status)
        {
            case SubscriptionStatus.Active:
                // Check if period has expired
                if (subscription.CurrentPeriodEnd < now)
                {
                    return new SubscriptionValidationResult(
                        IsValid: false,
                        ErrorCode: ErrorCodes.SubscriptionExpired,
                        ErrorMessage: "Your subscription has expired. Please renew your subscription.",
                        Status: subscription.Status);
                }
                return new SubscriptionValidationResult(
                    IsValid: true,
                    ErrorCode: null,
                    ErrorMessage: null,
                    Status: subscription.Status);

            case SubscriptionStatus.Trialing:
                // Check if trial has expired
                if (subscription.TrialEnd.HasValue && subscription.TrialEnd.Value < now)
                {
                    return new SubscriptionValidationResult(
                        IsValid: false,
                        ErrorCode: ErrorCodes.TrialExpired,
                        ErrorMessage: "Your trial period has expired. Please subscribe to continue.",
                        Status: subscription.Status);
                }
                return new SubscriptionValidationResult(
                    IsValid: true,
                    ErrorCode: null,
                    ErrorMessage: null,
                    Status: subscription.Status);

            case SubscriptionStatus.PastDue:
                // Allow grace period operations but with warning
                // Check if within 7-day grace period
                var gracePeriodEnd = subscription.CurrentPeriodEnd.AddDays(7);
                if (gracePeriodEnd > now)
                {
                    return new SubscriptionValidationResult(
                        IsValid: true,
                        ErrorCode: null,
                        ErrorMessage: null,
                        Status: subscription.Status);
                }
                return new SubscriptionValidationResult(
                    IsValid: false,
                    ErrorCode: ErrorCodes.SubscriptionExpired,
                    ErrorMessage: "Your subscription is past due and grace period has ended. Please update your payment method.",
                    Status: subscription.Status);

            case SubscriptionStatus.Cancelled:
                return new SubscriptionValidationResult(
                    IsValid: false,
                    ErrorCode: ErrorCodes.SubscriptionCancelled,
                    ErrorMessage: "Your subscription has been cancelled. Please resubscribe to continue.",
                    Status: subscription.Status);

            case SubscriptionStatus.Expired:
                return new SubscriptionValidationResult(
                    IsValid: false,
                    ErrorCode: ErrorCodes.SubscriptionExpired,
                    ErrorMessage: "Your subscription has expired. Please renew your subscription.",
                    Status: subscription.Status);

            case SubscriptionStatus.Paused:
                return new SubscriptionValidationResult(
                    IsValid: false,
                    ErrorCode: ErrorCodes.SubscriptionExpired,
                    ErrorMessage: "Your subscription is paused. Please resume your subscription to continue.",
                    Status: subscription.Status);

            default:
                _logger.LogWarning("Unknown subscription status: {Status} for organization {OrgId}",
                    subscription.Status, orgId);
                return new SubscriptionValidationResult(
                    IsValid: false,
                    ErrorCode: ErrorCodes.SubscriptionRequired,
                    ErrorMessage: "Subscription status is invalid. Please contact support.",
                    Status: subscription.Status);
        }
    }

    public async Task<int> GetEffectiveGstinCountAsync(Guid orgId)
    {
        // Check if this is a CA operator organization
        var isCaOperator = await IsCaOperatorOrganizationAsync(orgId);

        if (isCaOperator)
        {
            return await GetCaClientGstinCountAsync(orgId);
        }

        // Regular organization: count OrganizationGstins
        return await _dbContext.OrganizationGstins
            .CountAsync(g => g.OrganizationId == orgId && g.Status == "active" && g.DeletedAt == null);
    }

    public async Task<bool> ValidateGstinBelongsToOrgAsync(Guid orgId, string gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin))
            return false;

        gstin = gstin.Trim().ToUpperInvariant();

        // Check OrganizationGstins
        // Note: GSTIN is encrypted, so we need to load and compare in memory
        var gstins = await _dbContext.OrganizationGstins
            .Where(g => g.OrganizationId == orgId && g.Status == "active" && g.DeletedAt == null)
            .Select(g => g.Gstin)
            .ToListAsync();

        return gstins.Any(g => string.Equals(g, gstin, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<GstinLimitStatusDto> GetLimitStatusAsync(Guid orgId)
    {
        var planLimits = await GetPlanLimitsAsync(orgId);

        if (planLimits == null)
        {
            return new GstinLimitStatusDto(
                CurrentCount: 0,
                Limit: 0,
                IsUnlimited: false,
                CanAddMore: false);
        }

        var currentCount = await GetEffectiveGstinCountAsync(orgId);
        var limit = planLimits.GstinsAllowed;
        var isUnlimited = limit == -1;
        var canAddMore = isUnlimited || currentCount < limit;

        return new GstinLimitStatusDto(
            CurrentCount: currentCount,
            Limit: limit,
            IsUnlimited: isUnlimited,
            CanAddMore: canAddMore);
    }

    public async Task<IAsyncDisposable?> AcquireGstinAdditionLockAsync(Guid orgId, TimeSpan timeout)
    {
        try
        {
            var db = _redis.GetDatabase();
            var lockKey = $"{LockKeyPrefix}{orgId}";
            var lockValue = Guid.NewGuid().ToString();

            var acquired = await db.StringSetAsync(lockKey, lockValue, timeout, When.NotExists);

            if (!acquired)
            {
                _logger.LogWarning("Failed to acquire GSTIN addition lock for organization {OrgId}", orgId);
                return null;
            }

            return new RedisLockReleaser(db, lockKey, lockValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring GSTIN addition lock for organization {OrgId}", orgId);
            // In case of Redis failure, allow the operation to proceed
            // The database will handle the concurrency via transactions
            return new NoOpDisposable();
        }
    }

    private async Task<PlanLimits?> GetPlanLimitsAsync(Guid orgId)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.DeletedAt == null);

        return subscription?.Plan?.Limits;
    }

    private async Task<bool> IsCaOperatorOrganizationAsync(Guid orgId)
    {
        // Check if the organization's subscription plan is a CA operator plan
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.DeletedAt == null);

        return subscription?.Plan?.IsCaOperatorPlan ?? false;
    }

    private async Task<int> GetCaClientGstinCountAsync(Guid orgId)
    {
        // Get the CA user who owns this organization
        var caUserId = await _dbContext.OrganizationMembers
            .Where(m => m.OrganizationId == orgId && m.Role == "owner" && m.Status == "active" && m.DeletedAt == null)
            .Select(m => m.UserId)
            .FirstOrDefaultAsync();

        if (caUserId == default)
            return 0;

        // Count staging prospect clients (not rejected/withdrawn)
        var stagingCount = await _dbContext.CaProspectClients
            .CountAsync(p => p.CaUserId == caUserId && p.Status == "staging" && p.DeletedAt == null);

        // Count active CA-client relationships (OrganizationMember with role="ca")
        var activeCount = await _dbContext.OrganizationMembers
            .CountAsync(m => m.UserId == caUserId && m.Role == "ca" && m.Status == "active" && m.DeletedAt == null
                && (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow));

        // Total client GSTINs = staging + active
        // Note: A GSTIN cannot be in both staging and active state for the same CA,
        // as accepting an invitation transitions it from staging to active.
        return stagingCount + activeCount;
    }

    /// <summary>
    /// Helper class to release Redis lock on disposal.
    /// </summary>
    private class RedisLockReleaser : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _lockKey;
        private readonly string _lockValue;
        private bool _disposed;

        public RedisLockReleaser(IDatabase db, string lockKey, string lockValue)
        {
            _db = db;
            _lockKey = lockKey;
            _lockValue = lockValue;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Only delete if we still own the lock
                var script = @"
                    if redis.call('get', KEYS[1]) == ARGV[1] then
                        return redis.call('del', KEYS[1])
                    else
                        return 0
                    end";

                await _db.ScriptEvaluateAsync(script, new RedisKey[] { _lockKey }, new RedisValue[] { _lockValue });
            }
            catch
            {
                // Ignore errors during lock release
            }
        }
    }

    /// <summary>
    /// No-op disposable for when Redis lock acquisition fails.
    /// </summary>
    private class NoOpDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
