using EffortlessInsight.Api.Constants;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.Text.Json;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Represents a reserved notice slot that auto-rollbacks on dispose if not confirmed.
/// </summary>
public class NoticeReservation : IAsyncDisposable
{
    private readonly IDatabase _redisDb;
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly string _lockKey;
    private readonly string _lockValue;
    private readonly ILogger _logger;
    private bool _disposed;

    public Guid OrganizationId { get; }
    public Guid ReservationId { get; }
    internal bool IsConfirmed { get; set; }

    internal NoticeReservation(
        Guid organizationId,
        IDatabase redisDb,
        ApplicationDbContext dbContext,
        IDistributedCache cache,
        string lockKey,
        string lockValue,
        ILogger logger)
    {
        OrganizationId = organizationId;
        ReservationId = Guid.NewGuid();
        _redisDb = redisDb;
        _dbContext = dbContext;
        _cache = cache;
        _lockKey = lockKey;
        _lockValue = lockValue;
        _logger = logger;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // Rollback: decrement the counter if not confirmed
            if (!IsConfirmed)
            {
                await RollbackAsync();
            }
        }
        finally
        {
            // Always release the lock
            await ReleaseLockAsync();
        }
    }

    private async Task RollbackAsync()
    {
        try
        {
            _logger.LogInformation(
                "Rolling back notice reservation {ReservationId} for organization {OrganizationId}",
                ReservationId, OrganizationId);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var usage = await _dbContext.UsageRecords
                .FirstOrDefaultAsync(u =>
                    u.OrganizationId == OrganizationId &&
                    u.PeriodStart <= today &&
                    u.PeriodEnd >= today);

            if (usage != null && usage.NoticesCount > 0)
            {
                usage.NoticesCount--;
                usage.LastCalculatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                // Invalidate cache
                try
                {
                    await _cache.RemoveAsync($"billing:usage:{OrganizationId}");
                }
                catch
                {
                    // Ignore cache errors
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to rollback notice reservation {ReservationId} for organization {OrganizationId}",
                ReservationId, OrganizationId);
        }
    }

    private async Task ReleaseLockAsync()
    {
        try
        {
            // Only delete if we still own the lock (using Lua script for atomicity)
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            await _redisDb.ScriptEvaluateAsync(script, new RedisKey[] { _lockKey }, new RedisValue[] { _lockValue });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release notice quota lock for organization {OrganizationId}", OrganizationId);
        }
    }
}

/// <summary>
/// Implementation of the usage service.
/// </summary>
public class UsageService : IUsageService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<UsageService> _logger;

    private const string UsageCacheKeyPrefix = "billing:usage:";
    private const string NoticeQuotaLockKeyPrefix = "notice_quota_lock:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    public UsageService(
        ApplicationDbContext dbContext,
        IDistributedCache cache,
        IConnectionMultiplexer redis,
        ILogger<UsageService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _redis = redis;
        _logger = logger;
    }

    public async Task<UsageRecord?> GetCurrentUsageAsync(Guid organizationId)
    {
        var cacheKey = $"{UsageCacheKeyPrefix}{organizationId}";

        // Try cache first
        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                return JsonSerializer.Deserialize<UsageRecord>(cached);
            }
            catch
            {
                // Ignore cache errors
            }
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var usage = await _dbContext.UsageRecords
            .FirstOrDefaultAsync(u =>
                u.OrganizationId == organizationId &&
                u.PeriodStart <= today &&
                u.PeriodEnd >= today);

        if (usage != null)
        {
            try
            {
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(usage),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = CacheDuration
                    });
            }
            catch
            {
                // Ignore cache errors
            }
        }

        return usage;
    }

    public async Task<UsageRecord> GetOrCreateCurrentUsageAsync(Guid organizationId)
    {
        var usage = await GetCurrentUsageAsync(organizationId);
        if (usage != null)
            return usage;

        // Get subscription to determine billing period
        var subscription = await _dbContext.BillingSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId);

        DateOnly periodStart, periodEnd;
        if (subscription != null)
        {
            periodStart = DateOnly.FromDateTime(subscription.CurrentPeriodStart);
            periodEnd = DateOnly.FromDateTime(subscription.CurrentPeriodEnd);
        }
        else
        {
            // Default to calendar month
            var now = DateTime.UtcNow;
            periodStart = new DateOnly(now.Year, now.Month, 1);
            periodEnd = periodStart.AddMonths(1).AddDays(-1);
        }

        usage = new UsageRecord
        {
            OrganizationId = organizationId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            LastCalculatedAt = DateTime.UtcNow
        };

        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        await InvalidateCacheAsync(organizationId);

        return usage;
    }

    public async Task IncrementNoticeCountAsync(Guid organizationId)
    {
        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        usage.NoticesCount++;
        usage.LastCalculatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await InvalidateCacheAsync(organizationId);
    }

    public async Task DecrementNoticeCountAsync(Guid organizationId)
    {
        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        if (usage.NoticesCount > 0)
        {
            usage.NoticesCount--;
            usage.LastCalculatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            await InvalidateCacheAsync(organizationId);
        }
    }

    public async Task UpdateUserCountAsync(Guid organizationId, int count)
    {
        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        usage.UsersCount = count;
        usage.LastCalculatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await InvalidateCacheAsync(organizationId);
    }

    public async Task UpdateStorageUsageAsync(Guid organizationId, long bytesChange)
    {
        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        usage.StorageBytes = Math.Max(0, usage.StorageBytes + bytesChange);
        usage.LastCalculatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await InvalidateCacheAsync(organizationId);
    }

    public async Task IncrementApiCallsAsync(Guid organizationId)
    {
        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        usage.ApiCalls++;
        await _dbContext.SaveChangesAsync();
        await InvalidateCacheAsync(organizationId);
    }

    public async Task<(bool CanCreate, string? Reason)> CanCreateNoticeAsync(Guid organizationId)
    {
        var limits = await GetPlanLimitsAsync(organizationId);
        if (limits == null)
            return (false, "No active subscription");

        if (limits.NoticesPerMonth == -1)
            return (true, null);

        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        if (usage.NoticesCount >= limits.NoticesPerMonth)
            return (false, $"Monthly notice limit of {limits.NoticesPerMonth} reached. Please upgrade your plan.");

        return (true, null);
    }

    public async Task<(bool CanAdd, string? Reason)> CanAddUserAsync(Guid organizationId)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId);

        if (subscription == null)
            return (false, "No active subscription");

        var limits = subscription.Plan.Limits;
        if (limits.Users == -1)
            return (true, null);

        var totalSeats = subscription.SeatsIncluded + subscription.SeatsAdditional;
        var usage = await GetOrCreateCurrentUsageAsync(organizationId);

        if (usage.UsersCount >= totalSeats)
        {
            if (limits.AdditionalUsersAllowed)
                return (false, "User limit reached. Please add more seats to invite users.");
            return (false, $"User limit of {totalSeats} reached. Please upgrade your plan.");
        }

        return (true, null);
    }

    public async Task<(bool CanUpload, string? Reason)> CanUploadFileAsync(Guid organizationId, long fileSize)
    {
        var limits = await GetPlanLimitsAsync(organizationId);
        if (limits == null)
            return (false, "No active subscription");

        if (limits.StorageGb == -1)
            return (true, null);

        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        var limitBytes = limits.StorageGb * 1024L * 1024 * 1024;
        var newTotal = usage.StorageBytes + fileSize;

        if (newTotal > limitBytes)
        {
            var usedGb = usage.StorageBytes / (1024.0 * 1024 * 1024);
            return (false, $"Storage limit of {limits.StorageGb}GB reached (used: {usedGb:F1}GB). Please upgrade your plan.");
        }

        return (true, null);
    }

    public async Task<(bool CanCall, string? Reason)> CanMakeApiCallAsync(Guid organizationId)
    {
        var limits = await GetPlanLimitsAsync(organizationId);
        if (limits == null)
            return (false, "No active subscription");

        if (limits.ApiCalls == -1)
            return (true, null);

        var usage = await GetOrCreateCurrentUsageAsync(organizationId);
        if (usage.ApiCalls >= limits.ApiCalls)
            return (false, $"Monthly API call limit of {limits.ApiCalls} reached.");

        return (true, null);
    }

    public async Task<int> GetUsagePercentageAsync(Guid organizationId, string metric)
    {
        var limits = await GetPlanLimitsAsync(organizationId);
        if (limits == null)
            return 0;

        var usage = await GetCurrentUsageAsync(organizationId);
        if (usage == null)
            return 0;

        return metric.ToLowerInvariant() switch
        {
            "notices" => limits.NoticesPerMonth > 0 ? usage.NoticesCount * 100 / limits.NoticesPerMonth : 0,
            "users" => limits.Users > 0 ? usage.UsersCount * 100 / limits.Users : 0,
            "storage" => limits.StorageGb > 0
                ? (int)(usage.StorageBytes * 100 / (limits.StorageGb * 1024L * 1024 * 1024))
                : 0,
            "api" => limits.ApiCalls > 0 ? usage.ApiCalls * 100 / limits.ApiCalls : 0,
            _ => 0
        };
    }

    public async Task ResetUsageForPeriodAsync(Guid organizationId, DateOnly periodStart, DateOnly periodEnd)
    {
        var usage = new UsageRecord
        {
            OrganizationId = organizationId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            NoticesCount = 0,
            UsersCount = 0,
            StorageBytes = 0, // Storage carries over, will be recalculated
            ApiCalls = 0,
            LastCalculatedAt = DateTime.UtcNow
        };

        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();
        await InvalidateCacheAsync(organizationId);

        // Recalculate persistent metrics (storage, users)
        await RecalculateUsageAsync(organizationId);
    }

    public async Task RecalculateUsageAsync(Guid organizationId)
    {
        var usage = await GetOrCreateCurrentUsageAsync(organizationId);

        // Count active users
        var userCount = await _dbContext.OrganizationMembers
            .CountAsync(m => m.OrganizationId == organizationId && m.Status == "active");
        usage.UsersCount = userCount;

        // Calculate storage (sum of all file sizes)
        var storageBytes = await _dbContext.NoticeFiles
            .Where(f => f.OrganizationId == organizationId)
            .SumAsync(f => (long?)f.SizeBytes) ?? 0;
        usage.StorageBytes = storageBytes;

        // Count notices created this period
        var periodStart = DateTime.SpecifyKind(usage.PeriodStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(usage.PeriodEnd.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
        var noticeCount = await _dbContext.Notices
            .CountAsync(n =>
                n.OrganizationId == organizationId &&
                n.CreatedAt >= periodStart &&
                n.CreatedAt <= periodEnd);
        usage.NoticesCount = noticeCount;

        usage.LastCalculatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await InvalidateCacheAsync(organizationId);

        _logger.LogInformation(
            "Recalculated usage for organization {OrganizationId}: {Notices} notices, {Users} users, {Storage}MB storage",
            organizationId, usage.NoticesCount, usage.UsersCount, usage.StorageBytes / (1024 * 1024));
    }

    private async Task<PlanLimits?> GetPlanLimitsAsync(Guid organizationId)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId);

        return subscription?.Plan.Limits;
    }

    private async Task InvalidateCacheAsync(Guid organizationId)
    {
        try
        {
            await _cache.RemoveAsync($"{UsageCacheKeyPrefix}{organizationId}");
        }
        catch
        {
            // Ignore cache errors
        }
    }

    public async Task<(bool CanAdd, string? Reason)> CanAddGstinAsync(Guid organizationId)
    {
        var limits = await GetPlanLimitsAsync(organizationId);
        if (limits == null)
            return (false, "No active subscription");

        // -1 means unlimited
        if (limits.GstinsAllowed == -1)
            return (true, null);

        var currentCount = await GetGstinCountAsync(organizationId);
        if (currentCount >= limits.GstinsAllowed)
        {
            return (false, $"GSTIN limit of {limits.GstinsAllowed} reached. Upgrade your plan to add more GSTINs.");
        }

        return (true, null);
    }

    public async Task<int> GetGstinCountAsync(Guid organizationId)
    {
        return await _dbContext.OrganizationGstins
            .CountAsync(g => g.OrganizationId == organizationId && g.Status == "active");
    }

    public async Task<(bool CanDowngrade, string? Reason, int CurrentCount, int NewLimit)>
        ValidateGstinLimitForPlanChangeAsync(Guid organizationId, string newPlanCode)
    {
        // Get the new plan
        var newPlan = await _dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Code == newPlanCode && p.DeletedAt == null);

        if (newPlan == null)
            return (false, $"Plan '{newPlanCode}' not found", 0, 0);

        var newLimit = newPlan.Limits.GstinsAllowed;

        // -1 means unlimited, so any count is fine
        if (newLimit == -1)
            return (true, null, 0, -1);

        var currentCount = await GetGstinCountAsync(organizationId);

        if (currentCount > newLimit)
        {
            return (false,
                $"Cannot change to {newPlan.Name}: you have {currentCount} GSTIN(s) but this plan allows only {newLimit}. " +
                $"Please remove {currentCount - newLimit} GSTIN(s) first.",
                currentCount, newLimit);
        }

        return (true, null, currentCount, newLimit);
    }

    public async Task<NoticeReservationResult> TryReserveNoticeSlotAsync(Guid organizationId, TimeSpan? timeout = null)
    {
        var lockTimeout = timeout ?? DefaultLockTimeout;
        var lockKey = $"{NoticeQuotaLockKeyPrefix}{organizationId}";
        var lockValue = Guid.NewGuid().ToString();

        try
        {
            var db = _redis.GetDatabase();

            // Acquire distributed lock
            var acquired = await db.StringSetAsync(lockKey, lockValue, lockTimeout, When.NotExists);
            if (!acquired)
            {
                _logger.LogWarning(
                    "Failed to acquire notice quota lock for organization {OrganizationId} - concurrent operation in progress",
                    organizationId);
                return new NoticeReservationResult(
                    Success: false,
                    ErrorCode: ErrorCodes.ConcurrentOperation,
                    ErrorMessage: "Another notice creation is in progress. Please try again.",
                    Reservation: null);
            }

            try
            {
                // Check quota inside lock
                var (canCreate, quotaReason) = await CanCreateNoticeAsync(organizationId);
                if (!canCreate)
                {
                    // Release lock immediately on quota failure
                    await db.KeyDeleteAsync(lockKey);
                    return new NoticeReservationResult(
                        Success: false,
                        ErrorCode: ErrorCodes.NoticeLimitExceeded,
                        ErrorMessage: quotaReason ?? "Monthly notice limit reached. Please upgrade your plan.",
                        Reservation: null);
                }

                // Pre-increment counter (pessimistic reservation)
                await IncrementNoticeCountAsync(organizationId);

                // Create reservation handle
                var reservation = new NoticeReservation(
                    organizationId,
                    db,
                    _dbContext,
                    _cache,
                    lockKey,
                    lockValue,
                    _logger);

                _logger.LogInformation(
                    "Notice slot reserved {ReservationId} for organization {OrganizationId}",
                    reservation.ReservationId, organizationId);

                return new NoticeReservationResult(
                    Success: true,
                    ErrorCode: null,
                    ErrorMessage: null,
                    Reservation: reservation);
            }
            catch (Exception ex)
            {
                // On any error, release the lock
                try
                {
                    await db.KeyDeleteAsync(lockKey);
                }
                catch
                {
                    // Ignore lock release errors
                }
                throw;
            }
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogError(ex,
                "Redis connection error during notice reservation for organization {OrganizationId}. " +
                "Falling back to non-atomic check.",
                organizationId);

            // Fallback: perform non-atomic check (same as before)
            // This maintains availability when Redis is down
            var (canCreate, quotaReason) = await CanCreateNoticeAsync(organizationId);
            if (!canCreate)
            {
                return new NoticeReservationResult(
                    Success: false,
                    ErrorCode: ErrorCodes.NoticeLimitExceeded,
                    ErrorMessage: quotaReason ?? "Monthly notice limit reached. Please upgrade your plan.",
                    Reservation: null);
            }

            // Increment counter
            await IncrementNoticeCountAsync(organizationId);

            // Create a no-op reservation (no lock to release, no rollback possible)
            var fallbackReservation = new NoOpNoticeReservation(organizationId, _logger);

            return new NoticeReservationResult(
                Success: true,
                ErrorCode: null,
                ErrorMessage: null,
                Reservation: fallbackReservation);
        }
    }

    public Task ConfirmNoticeReservationAsync(NoticeReservation reservation)
    {
        if (reservation == null)
            throw new ArgumentNullException(nameof(reservation));

        reservation.IsConfirmed = true;

        _logger.LogInformation(
            "Notice reservation {ReservationId} confirmed for organization {OrganizationId}",
            reservation.ReservationId, reservation.OrganizationId);

        return Task.CompletedTask;
    }
}

/// <summary>
/// No-op reservation used when Redis is unavailable.
/// Does not support rollback - marked as confirmed immediately.
/// </summary>
internal sealed class NoOpNoticeReservation : NoticeReservation
{
    internal NoOpNoticeReservation(Guid organizationId, ILogger logger)
        : base(organizationId, null!, null!, null!, "", "", logger)
    {
        // Mark as confirmed immediately since we can't rollback without Redis
        IsConfirmed = true;
    }
}
