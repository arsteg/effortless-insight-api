using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Service for fetching and processing notices from the GST portal.
/// </summary>
public interface IGstnNoticeService
{
    /// <summary>
    /// Syncs notices from the GST portal for a connection.
    /// </summary>
    Task<GstnSyncResult> SyncNoticesAsync(
        Guid connectionId,
        GstnSyncOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and stores a notice document from the portal.
    /// </summary>
    Task<GstnDocumentDownloadResult> DownloadNoticeDocumentAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync logs for a connection.
    /// </summary>
    Task<List<GstnSyncLogDto>> GetSyncLogsAsync(
        Guid connectionId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a notice already exists by GSTN ID.
    /// </summary>
    Task<bool> NoticeExistsAsync(
        Guid organizationId,
        string gstnNoticeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for sync operation.
/// </summary>
public class GstnSyncOptions
{
    /// <summary>
    /// Start date for notice fetch (defaults to last sync date or 1 year back).
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// End date for notice fetch (defaults to now).
    /// </summary>
    public DateTime? ToDate { get; set; }

    /// <summary>
    /// Type of sync operation.
    /// </summary>
    public string SyncType { get; set; } = GstnSyncType.Incremental;

    /// <summary>
    /// What triggered this sync.
    /// </summary>
    public string TriggerSource { get; set; } = GstnSyncTrigger.Scheduled;

    /// <summary>
    /// User who triggered the sync (for manual syncs).
    /// </summary>
    public Guid? TriggeredById { get; set; }

    /// <summary>
    /// Maximum notices to process.
    /// </summary>
    public int? MaxNotices { get; set; }
}

/// <summary>
/// Result of a sync operation.
/// </summary>
public record GstnSyncResult(
    bool Success,
    Guid? SyncLogId,
    int NoticesFound,
    int NoticesImported,
    int NoticesSkipped,
    int NoticesFailed,
    string? ErrorCode,
    string? ErrorMessage,
    List<Guid>? ImportedNoticeIds
);

/// <summary>
/// Result of document download.
/// </summary>
public record GstnDocumentDownloadResult(
    bool Success,
    string? FileUrl,
    string? FileName,
    long? FileSize,
    string? ErrorCode,
    string? ErrorMessage
);

/// <summary>
/// DTO for sync log entries.
/// </summary>
public record GstnSyncLogDto(
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
