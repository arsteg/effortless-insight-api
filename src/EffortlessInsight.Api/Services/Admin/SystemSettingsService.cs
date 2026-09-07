using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Implementation of the system settings service.
/// </summary>
public class SystemSettingsService : ISystemSettingsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SystemSettingsService> _logger;
    private const string CachePrefix = "system_setting:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public SystemSettingsService(
        ApplicationDbContext dbContext,
        IDistributedCache cache,
        ILogger<SystemSettingsService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        // Try cache first
        var cacheKey = $"{CachePrefix}{key}";
        var cached = await _cache.GetStringAsync(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        // Get from database
        var setting = await _dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key);

        if (setting == null)
        {
            return null;
        }

        // Cache the value
        await _cache.SetStringAsync(cacheKey, setting.Value, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration
        });

        return setting.Value;
    }

    public async Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false)
    {
        var value = await GetSettingAsync(key);
        if (value == null)
        {
            return defaultValue;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.Ordinal);
    }

    public async Task<int> GetIntSettingAsync(string key, int defaultValue = 0)
    {
        var value = await GetSettingAsync(key);
        if (value == null || !int.TryParse(value, out var intValue))
        {
            return defaultValue;
        }

        return intValue;
    }

    public async Task SetSettingAsync(string key, string value, Guid? adminId = null)
    {
        var setting = await _dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);

        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByAdminId = adminId
            };
            _dbContext.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
            setting.UpdatedByAdminId = adminId;
        }

        await _dbContext.SaveChangesAsync();

        // Invalidate cache
        var cacheKey = $"{CachePrefix}{key}";
        await _cache.RemoveAsync(cacheKey);

        _logger.LogInformation("System setting {Key} updated to {Value} by admin {AdminId}",
            key, value, adminId);
    }

    public async Task<Dictionary<string, string>> GetSettingsByCategoryAsync(string category)
    {
        var settings = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(s => s.Category == category)
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        return settings;
    }

    public async Task<BillingSettingsDto> GetBillingSettingsAsync()
    {
        return new BillingSettingsDto
        {
            AdditionalSeatsEnabled = await GetBoolSettingAsync(SystemSettingKeys.AdditionalSeatsEnabled, true),
            TrialEnabled = await GetBoolSettingAsync(SystemSettingKeys.TrialEnabled, true),
            DefaultTrialDays = await GetIntSettingAsync(SystemSettingKeys.DefaultTrialDays, 14),
            DowngradesAllowed = await GetBoolSettingAsync(SystemSettingKeys.DowngradesAllowed, false)
        };
    }

    public async Task UpdateBillingSettingsAsync(UpdateBillingSettingsRequest request, Guid adminId)
    {
        if (request.AdditionalSeatsEnabled.HasValue)
        {
            await SetSettingAsync(
                SystemSettingKeys.AdditionalSeatsEnabled,
                request.AdditionalSeatsEnabled.Value ? "true" : "false",
                adminId);
        }

        if (request.TrialEnabled.HasValue)
        {
            await SetSettingAsync(
                SystemSettingKeys.TrialEnabled,
                request.TrialEnabled.Value ? "true" : "false",
                adminId);
        }

        if (request.DefaultTrialDays.HasValue)
        {
            await SetSettingAsync(
                SystemSettingKeys.DefaultTrialDays,
                request.DefaultTrialDays.Value.ToString(),
                adminId);
        }

        if (request.DowngradesAllowed.HasValue)
        {
            await SetSettingAsync(
                SystemSettingKeys.DowngradesAllowed,
                request.DowngradesAllowed.Value ? "true" : "false",
                adminId);
        }
    }

    public async Task InitializeDefaultSettingsAsync()
    {
        var defaults = new List<(string Key, string Value, string Description, string DataType, string Category)>
        {
            (SystemSettingKeys.AdditionalSeatsEnabled, "true", "Enable per-seat pricing and additional seats feature", "boolean", "billing"),
            (SystemSettingKeys.TrialEnabled, "true", "Enable trial period for new signups", "boolean", "billing"),
            (SystemSettingKeys.DefaultTrialDays, "14", "Default trial period in days", "number", "billing"),
            (SystemSettingKeys.DowngradesAllowed, "false", "Allow users to downgrade to lower plans. When disabled, users can only upgrade.", "boolean", "billing"),
            (SystemSettingKeys.MaintenanceMode, "false", "Enable maintenance mode", "boolean", "system")
        };

        foreach (var (key, value, description, dataType, category) in defaults)
        {
            var exists = await _dbContext.SystemSettings.AnyAsync(s => s.Key == key);
            if (!exists)
            {
                _dbContext.SystemSettings.Add(new SystemSetting
                {
                    Key = key,
                    Value = value,
                    Description = description,
                    DataType = dataType,
                    Category = category,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("System settings initialized with defaults");
    }
}
