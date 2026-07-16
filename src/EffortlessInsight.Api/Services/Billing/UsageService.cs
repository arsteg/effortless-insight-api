using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of the usage service.
/// </summary>
public class UsageService : IUsageService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<UsageService> _logger;

    private const string UsageCacheKeyPrefix = "billing:usage:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public UsageService(
        ApplicationDbContext dbContext,
        IDistributedCache cache,
        ILogger<UsageService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
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
}
