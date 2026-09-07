using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing global system settings.
/// </summary>
[Route("api/v1/admin/system-settings")]
public class AdminSystemSettingsController : AdminControllerBase
{
    private readonly ISystemSettingsService _settingsService;
    private readonly IAdminAuditService _auditService;

    public AdminSystemSettingsController(
        ISystemSettingsService settingsService,
        IAdminAuditService auditService,
        ILogger<AdminSystemSettingsController> logger)
        : base(logger)
    {
        _settingsService = settingsService;
        _auditService = auditService;
    }

    /// <summary>
    /// Gets billing-related system settings.
    /// </summary>
    [HttpGet("billing")]
    [ProducesResponseType(typeof(ApiResponse<BillingSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBillingSettings()
    {
        if (!HasPermission(AdminPermissions.SettingsView))
        {
            return Forbid();
        }

        var settings = await _settingsService.GetBillingSettingsAsync();
        return Success(settings);
    }

    /// <summary>
    /// Updates billing-related system settings.
    /// </summary>
    [HttpPut("billing")]
    [ProducesResponseType(typeof(ApiResponse<BillingSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateBillingSettings([FromBody] UpdateBillingSettingsRequest request)
    {
        if (!HasPermission(AdminPermissions.SettingsUpdate))
        {
            return Forbid();
        }

        await _settingsService.UpdateBillingSettingsAsync(request, CurrentAdminId);

        // Audit log
        await _auditService.LogAsync(
            CurrentAdminId,
            "billing_settings_updated",
            "SystemSettings",
            null,
            $"Updated billing settings: {System.Text.Json.JsonSerializer.Serialize(request)}");

        var settings = await _settingsService.GetBillingSettingsAsync();
        return Success(settings, "Billing settings updated successfully");
    }

    /// <summary>
    /// Gets a specific setting by key.
    /// </summary>
    [HttpGet("{key}")]
    [ProducesResponseType(typeof(ApiResponse<SystemSettingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSetting(string key)
    {
        if (!HasPermission(AdminPermissions.SettingsView))
        {
            return Forbid();
        }

        var value = await _settingsService.GetSettingAsync(key);
        if (value == null)
        {
            return NotFoundResponse($"Setting '{key}' not found");
        }

        return Success(new SystemSettingResponse { Key = key, Value = value });
    }

    /// <summary>
    /// Sets a specific setting value.
    /// </summary>
    [HttpPut("{key}")]
    [ProducesResponseType(typeof(ApiResponse<SystemSettingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetSetting(string key, [FromBody] SetSettingRequest request)
    {
        if (!HasPermission(AdminPermissions.SettingsUpdate))
        {
            return Forbid();
        }

        await _settingsService.SetSettingAsync(key, request.Value, CurrentAdminId);

        // Audit log
        await _auditService.LogAsync(
            CurrentAdminId,
            "system_setting_updated",
            "SystemSettings",
            null,
            $"Updated setting '{key}' to '{request.Value}'");

        return Success(new SystemSettingResponse { Key = key, Value = request.Value }, "Setting updated successfully");
    }
}

/// <summary>
/// Response for a single system setting.
/// </summary>
public record SystemSettingResponse
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Request to set a setting value.
/// </summary>
public record SetSettingRequest
{
    public string Value { get; init; } = string.Empty;
}
