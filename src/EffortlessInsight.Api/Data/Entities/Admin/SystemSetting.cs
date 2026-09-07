using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Stores global system settings as key-value pairs.
/// </summary>
public class SystemSetting
{
    /// <summary>
    /// Unique key for the setting.
    /// </summary>
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Setting value (stored as string, parsed by application).
    /// </summary>
    [MaxLength(1000)]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the setting.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Data type hint for UI (boolean, string, number, json).
    /// </summary>
    [MaxLength(20)]
    public string DataType { get; set; } = "string";

    /// <summary>
    /// Category for grouping settings in UI.
    /// </summary>
    [MaxLength(50)]
    public string Category { get; set; } = "general";

    /// <summary>
    /// When the setting was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who last modified the setting.
    /// </summary>
    public Guid? UpdatedByAdminId { get; set; }
}

/// <summary>
/// Well-known system setting keys.
/// </summary>
public static class SystemSettingKeys
{
    /// <summary>
    /// Whether per-seat pricing / additional seats feature is enabled globally.
    /// </summary>
    public const string AdditionalSeatsEnabled = "billing.additional_seats_enabled";

    /// <summary>
    /// Whether trial is enabled for new signups.
    /// </summary>
    public const string TrialEnabled = "billing.trial_enabled";

    /// <summary>
    /// Default trial duration in days.
    /// </summary>
    public const string DefaultTrialDays = "billing.default_trial_days";

    /// <summary>
    /// Whether maintenance mode is enabled.
    /// </summary>
    public const string MaintenanceMode = "system.maintenance_mode";

    /// <summary>
    /// Whether plan downgrades are allowed.
    /// When false, users can only upgrade to higher plans, not downgrade.
    /// </summary>
    public const string DowngradesAllowed = "billing.downgrades_allowed";
}
