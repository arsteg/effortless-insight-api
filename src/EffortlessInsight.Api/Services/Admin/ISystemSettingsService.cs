using EffortlessInsight.Api.Data.Entities.Admin;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for managing global system settings.
/// </summary>
public interface ISystemSettingsService
{
    /// <summary>
    /// Gets a setting value by key.
    /// </summary>
    Task<string?> GetSettingAsync(string key);

    /// <summary>
    /// Gets a setting value as boolean.
    /// </summary>
    Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false);

    /// <summary>
    /// Gets a setting value as integer.
    /// </summary>
    Task<int> GetIntSettingAsync(string key, int defaultValue = 0);

    /// <summary>
    /// Sets a setting value.
    /// </summary>
    Task SetSettingAsync(string key, string value, Guid? adminId = null);

    /// <summary>
    /// Gets all settings in a category.
    /// </summary>
    Task<Dictionary<string, string>> GetSettingsByCategoryAsync(string category);

    /// <summary>
    /// Gets all billing-related settings.
    /// </summary>
    Task<BillingSettingsDto> GetBillingSettingsAsync();

    /// <summary>
    /// Updates billing settings.
    /// </summary>
    Task UpdateBillingSettingsAsync(UpdateBillingSettingsRequest request, Guid adminId);

    /// <summary>
    /// Initializes default settings if they don't exist.
    /// </summary>
    Task InitializeDefaultSettingsAsync();
}

/// <summary>
/// Billing settings DTO.
/// </summary>
public record BillingSettingsDto
{
    /// <summary>
    /// Whether additional seats/per-seat pricing is enabled.
    /// </summary>
    public bool AdditionalSeatsEnabled { get; init; } = true;

    /// <summary>
    /// Whether trial is enabled for new signups.
    /// </summary>
    public bool TrialEnabled { get; init; } = true;

    /// <summary>
    /// Default trial duration in days.
    /// </summary>
    public int DefaultTrialDays { get; init; } = 14;

    /// <summary>
    /// Whether plan downgrades are allowed.
    /// When false, users can only upgrade to higher plans.
    /// </summary>
    public bool DowngradesAllowed { get; init; } = false;
}

/// <summary>
/// Request to update billing settings.
/// </summary>
public record UpdateBillingSettingsRequest
{
    /// <summary>
    /// Whether additional seats/per-seat pricing is enabled.
    /// </summary>
    public bool? AdditionalSeatsEnabled { get; init; }

    /// <summary>
    /// Whether trial is enabled for new signups.
    /// </summary>
    public bool? TrialEnabled { get; init; }

    /// <summary>
    /// Default trial duration in days.
    /// </summary>
    public int? DefaultTrialDays { get; init; }

    /// <summary>
    /// Whether plan downgrades are allowed.
    /// </summary>
    public bool? DowngradesAllowed { get; init; }
}
