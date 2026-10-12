using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Audit log for email sync operations, tracking each sync attempt and its results.
/// </summary>
public class EmailSyncLog : BaseEntity
{
    [Required]
    public Guid ConnectedEmailAccountId { get; set; }

    [ForeignKey(nameof(ConnectedEmailAccountId))]
    public ConnectedEmailAccount ConnectedEmailAccount { get; set; } = null!;

    /// <summary>
    /// Outcome of the sync operation.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = EmailSyncStatus.InProgress;

    /// <summary>
    /// What triggered this sync.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string TriggerSource { get; set; } = EmailSyncTrigger.Scheduled;

    /// <summary>
    /// User who triggered the sync (for manual syncs).
    /// </summary>
    public Guid? TriggeredById { get; set; }

    [ForeignKey(nameof(TriggeredById))]
    public ApplicationUser? TriggeredBy { get; set; }

    /// <summary>
    /// When the sync operation started.
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the sync operation completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration of the sync operation in milliseconds.
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Total emails scanned during this sync.
    /// </summary>
    public int EmailsScanned { get; set; }

    /// <summary>
    /// Notices successfully imported.
    /// </summary>
    public int NoticesImported { get; set; }

    /// <summary>
    /// Attachments skipped (wrong type, too large, etc.).
    /// </summary>
    public int AttachmentsSkipped { get; set; }

    /// <summary>
    /// Duplicate notices skipped.
    /// </summary>
    public int DuplicatesSkipped { get; set; }

    /// <summary>
    /// Notices that failed to import.
    /// </summary>
    public int NoticesFailed { get; set; }

    /// <summary>
    /// Number of times AI was used for uncertain detection.
    /// </summary>
    public int AiDetectionUsed { get; set; }

    /// <summary>
    /// Start of the email date range scanned.
    /// </summary>
    public DateTime? SyncPeriodFrom { get; set; }

    /// <summary>
    /// End of the email date range scanned.
    /// </summary>
    public DateTime? SyncPeriodTo { get; set; }

    /// <summary>
    /// Sync cursor used at start (for incremental sync).
    /// </summary>
    public string? CursorStart { get; set; }

    /// <summary>
    /// New sync cursor after completion.
    /// </summary>
    public string? CursorEnd { get; set; }

    /// <summary>
    /// Error message if sync failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed error information for debugging.
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// JSON list of notice IDs that were imported.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<Guid>? ImportedNoticeIds { get; set; }

    /// <summary>
    /// Additional metadata about the sync.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Email sync operation status constants.
/// </summary>
public static class EmailSyncStatus
{
    /// <summary>
    /// Sync is currently running.
    /// </summary>
    public const string InProgress = "in_progress";

    /// <summary>
    /// Sync completed successfully.
    /// </summary>
    public const string Completed = "completed";

    /// <summary>
    /// Sync completed with some warnings.
    /// </summary>
    public const string CompletedWithWarnings = "completed_with_warnings";

    /// <summary>
    /// Sync failed.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// Sync was cancelled.
    /// </summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Sync skipped due to quota limit.
    /// </summary>
    public const string QuotaExceeded = "quota_exceeded";

    public static readonly string[] All =
    [
        InProgress, Completed, CompletedWithWarnings, Failed, Cancelled, QuotaExceeded
    ];
}

/// <summary>
/// What triggered the email sync operation.
/// </summary>
public static class EmailSyncTrigger
{
    /// <summary>
    /// Triggered by scheduled background job.
    /// </summary>
    public const string Scheduled = "scheduled";

    /// <summary>
    /// Triggered manually by user.
    /// </summary>
    public const string Manual = "manual";

    /// <summary>
    /// Triggered by initial connection setup.
    /// </summary>
    public const string Initial = "initial";

    /// <summary>
    /// Retry after previous failure.
    /// </summary>
    public const string Retry = "retry";

    public static readonly string[] All =
    [
        Scheduled, Manual, Initial, Retry
    ];
}
