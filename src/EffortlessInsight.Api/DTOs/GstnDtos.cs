using System.ComponentModel.DataAnnotations;
using EffortlessInsight.Api.Services.GstnIntegration;

namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// GSTN Connection DTOs
// ============================================================================

/// <summary>
/// Response for listing GSTN connections.
/// </summary>
public record GstnConnectionListResponse(
    List<GstnConnectionDto> Connections,
    int Total
);

/// <summary>
/// DTO for a GSTN connection.
/// </summary>
public record GstnConnectionDto(
    Guid Id,
    Guid OrganizationGstinId,
    string Gstin,
    string? TradeName,
    string Status,
    bool IsConnected,
    string GspProvider,
    DateTime? ConnectedAt,
    DateTime? LastSyncAt,
    DateTime? NextScheduledSyncAt,
    bool AutoSyncEnabled,
    int SyncIntervalHours,
    int ConsecutiveFailures,
    string? LastSyncError,
    string? ConnectedByName
);

/// <summary>
/// Response for connection status.
/// This is an alias for GstnConnectionStatusDto from the service layer to maintain API consistency.
/// </summary>
public record GstnConnectionStatusResponse(
    Guid OrganizationGstinId,
    string Gstin,
    string Status,
    bool IsConnected,
    DateTime? ConnectedAt,
    DateTime? LastSyncAt,
    DateTime? NextScheduledSyncAt,
    bool AutoSyncEnabled,
    int SyncIntervalHours,
    int ConsecutiveFailures,
    string? LastSyncError,
    string? ConnectedByName
)
{
    /// <summary>
    /// Creates a GstnConnectionStatusResponse from a GstnConnectionStatusDto.
    /// </summary>
    public static GstnConnectionStatusResponse FromDto(GstnConnectionStatusDto dto) =>
        new(
            dto.OrganizationGstinId,
            dto.Gstin,
            dto.Status,
            dto.IsConnected,
            dto.ConnectedAt,
            dto.LastSyncAt,
            dto.NextScheduledSyncAt,
            dto.AutoSyncEnabled,
            dto.SyncIntervalHours,
            dto.ConsecutiveFailures,
            dto.LastSyncError,
            dto.ConnectedByName
        );
}

// ============================================================================
// OTP Flow DTOs
// ============================================================================

/// <summary>
/// Response from initiating GSTN connection.
/// </summary>
public record GstnConnectInitiateResponse(
    bool Success,
    string? OtpDestination,
    string? OtpDestinationType,
    DateTime? ExpiresAt,
    string? ErrorCode,
    string? ErrorMessage
);

/// <summary>
/// Request to verify OTP for GSTN connection.
/// </summary>
public record GstnVerifyOtpRequest(
    [Required]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be exactly 6 digits")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be 6 digits")]
    string Otp
);

/// <summary>
/// Response from OTP verification.
/// </summary>
public record GstnVerifyOtpResponse(
    bool Success,
    GstnConnectionStatusResponse? Connection,
    string? ErrorCode,
    string? ErrorMessage,
    int? RemainingAttempts
);

/// <summary>
/// Request to disconnect from GSTN portal.
/// </summary>
public record GstnDisconnectRequest(
    string? Reason
);

// ============================================================================
// Sync DTOs
// ============================================================================

/// <summary>
/// Response from manual sync trigger.
/// </summary>
public record GstnSyncTriggerResponse(
    bool Success,
    Guid? SyncLogId,
    int NoticesFound,
    int NoticesImported,
    int NoticesSkipped,
    int NoticesFailed,
    string? ErrorCode,
    string? ErrorMessage
);

/// <summary>
/// Response for sync history.
/// </summary>
public record GstnSyncHistoryResponse(
    List<GstnSyncLogEntryDto> Logs,
    int Total
);

/// <summary>
/// DTO for a sync log entry.
/// </summary>
public record GstnSyncLogEntryDto(
    Guid Id,
    string SyncType,
    string Status,
    string TriggerSource,
    DateTime StartedAt,
    DateTime? CompletedAt,
    long? DurationMs,
    int NoticesFound,
    int NoticesImported,
    int NoticesSkipped,
    int NoticesFailed,
    string? ErrorMessage,
    string? TriggeredByName
);

// ============================================================================
// Settings DTOs
// ============================================================================

/// <summary>
/// Request to update connection settings.
/// </summary>
public record GstnUpdateSettingsRequest(
    bool? AutoSyncEnabled,

    [Range(1, 24, ErrorMessage = "Sync interval must be between 1 and 24 hours")]
    int? SyncIntervalHours
);

/// <summary>
/// Response from updating settings.
/// </summary>
public record GstnSettingsResponse(
    bool AutoSyncEnabled,
    int SyncIntervalHours,
    DateTime? NextScheduledSyncAt
);
