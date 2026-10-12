using System.ComponentModel.DataAnnotations;
using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// EMAIL ACCOUNT DTOs
// ============================================================================

/// <summary>
/// Response containing list of connected email accounts.
/// </summary>
public record EmailAccountListResponse(
    List<EmailAccountDto> Accounts,
    int Total
);

/// <summary>
/// Connected email account information (no tokens exposed).
/// </summary>
public record EmailAccountDto(
    Guid Id,
    string Provider,
    string Email,
    string? DisplayName,
    string Status,
    bool IsConnected,
    bool AutoSyncEnabled,
    DateTime? LastSyncAt,
    string? LastSyncError,
    EmailSyncResultDto? LastSyncResult,
    DateTime ConnectedAt
)
{
    public static EmailAccountDto FromEntity(ConnectedEmailAccount account) => new(
        Id: account.Id,
        Provider: account.Provider,
        Email: account.Email,
        DisplayName: account.DisplayName,
        Status: account.Status,
        IsConnected: EmailAccountStatus.CanSync(account.Status),
        AutoSyncEnabled: account.AutoSyncEnabled,
        LastSyncAt: account.LastSyncAt,
        LastSyncError: account.LastError,
        LastSyncResult: account.LastSyncResult != null
            ? new EmailSyncResultDto(
                account.LastSyncResult.EmailsScanned,
                account.LastSyncResult.NoticesImported,
                account.LastSyncResult.AttachmentsSkipped,
                account.LastSyncResult.DuplicatesSkipped,
                account.LastSyncResult.FailedToProcess)
            : null,
        ConnectedAt: account.ConnectedAt ?? account.CreatedAt
    );
}

/// <summary>
/// Sync result summary.
/// </summary>
public record EmailSyncResultDto(
    int EmailsScanned,
    int NoticesImported,
    int AttachmentsSkipped,
    int DuplicatesSkipped,
    int FailedToProcess
);

// ============================================================================
// OAUTH FLOW DTOs
// ============================================================================

/// <summary>
/// Request to initiate OAuth connection.
/// </summary>
public record EmailConnectInitiateRequest(
    [Required]
    [RegularExpression("^(gmail|outlook)$", ErrorMessage = "Provider must be 'gmail' or 'outlook'")]
    string Provider
);

/// <summary>
/// Response with OAuth authorization URL.
/// </summary>
public record EmailConnectInitiateResponse(
    bool Success,
    string? AuthorizationUrl,
    string? State,
    string? ErrorCode,
    string? ErrorMessage
);

/// <summary>
/// Request to complete OAuth callback.
/// </summary>
public record EmailConnectCallbackRequest(
    [Required]
    string Code,

    [Required]
    string State
);

/// <summary>
/// Response after OAuth completion.
/// </summary>
public record EmailConnectCallbackResponse(
    bool Success,
    EmailAccountDto? Account,
    string? ErrorCode,
    string? ErrorMessage
);

// ============================================================================
// SYNC DTOs
// ============================================================================

/// <summary>
/// Response after triggering a sync.
/// </summary>
public record EmailSyncTriggerResponse(
    bool Success,
    Guid? SyncLogId,
    int EmailsScanned,
    int NoticesImported,
    int AttachmentsSkipped,
    int DuplicatesSkipped,
    int NoticesFailed,
    string? ErrorCode,
    string? ErrorMessage
);

/// <summary>
/// Sync history entry.
/// </summary>
public record EmailSyncLogDto(
    Guid Id,
    string Status,
    string TriggerSource,
    DateTime StartedAt,
    DateTime? CompletedAt,
    long? DurationMs,
    int EmailsScanned,
    int NoticesImported,
    int AttachmentsSkipped,
    int DuplicatesSkipped,
    int NoticesFailed,
    int AiDetectionUsed,
    string? ErrorMessage,
    string? TriggeredByName,
    List<Guid>? ImportedNoticeIds
)
{
    public static EmailSyncLogDto FromEntity(EmailSyncLog log, string? triggeredByName = null) => new(
        Id: log.Id,
        Status: log.Status,
        TriggerSource: log.TriggerSource,
        StartedAt: log.StartedAt,
        CompletedAt: log.CompletedAt,
        DurationMs: log.DurationMs,
        EmailsScanned: log.EmailsScanned,
        NoticesImported: log.NoticesImported,
        AttachmentsSkipped: log.AttachmentsSkipped,
        DuplicatesSkipped: log.DuplicatesSkipped,
        NoticesFailed: log.NoticesFailed,
        AiDetectionUsed: log.AiDetectionUsed,
        ErrorMessage: log.ErrorMessage,
        TriggeredByName: triggeredByName,
        ImportedNoticeIds: log.ImportedNoticeIds
    );
}

/// <summary>
/// Response containing sync history.
/// </summary>
public record EmailSyncHistoryResponse(
    List<EmailSyncLogDto> Logs,
    int Total
);

// ============================================================================
// SETTINGS DTOs
// ============================================================================

/// <summary>
/// Request to update account settings.
/// </summary>
public record EmailAccountSettingsRequest(
    bool? AutoSyncEnabled
);

/// <summary>
/// Response after updating settings.
/// </summary>
public record EmailAccountSettingsResponse(
    bool AutoSyncEnabled
);
