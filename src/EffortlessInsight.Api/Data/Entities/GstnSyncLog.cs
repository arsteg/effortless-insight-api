using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Audit log for GSTN sync operations, tracking each sync attempt and its results.
/// </summary>
public class GstnSyncLog : BaseEntity
{
    [Required]
    public Guid GstnConnectionId { get; set; }

    [ForeignKey(nameof(GstnConnectionId))]
    public GstnConnection GstnConnection { get; set; } = null!;

    /// <summary>
    /// Type of sync operation performed.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string SyncType { get; set; } = GstnSyncType.Notices;

    /// <summary>
    /// Outcome of the sync operation.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = GstnSyncStatus.InProgress;

    /// <summary>
    /// What triggered this sync.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string TriggerSource { get; set; } = GstnSyncTrigger.Scheduled;

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
    /// Total notices found from the portal.
    /// </summary>
    public int NoticesFound { get; set; }

    /// <summary>
    /// Notices successfully imported.
    /// </summary>
    public int NoticesImported { get; set; }

    /// <summary>
    /// Notices skipped (duplicates or filtered).
    /// </summary>
    public int NoticesSkipped { get; set; }

    /// <summary>
    /// Notices that failed to import.
    /// </summary>
    public int NoticesFailed { get; set; }

    /// <summary>
    /// Start date of the sync period.
    /// </summary>
    public DateTime? SyncPeriodFrom { get; set; }

    /// <summary>
    /// End date of the sync period.
    /// </summary>
    public DateTime? SyncPeriodTo { get; set; }

    /// <summary>
    /// Error message if sync failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed error information for debugging.
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// GSP request correlation ID for troubleshooting.
    /// </summary>
    [MaxLength(100)]
    public string? GspCorrelationId { get; set; }

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
/// Types of sync operations.
/// </summary>
public static class GstnSyncType
{
    /// <summary>
    /// Sync notices from the portal.
    /// </summary>
    public const string Notices = "notices";

    /// <summary>
    /// Sync returns data.
    /// </summary>
    public const string Returns = "returns";

    /// <summary>
    /// Full data sync.
    /// </summary>
    public const string Full = "full";

    /// <summary>
    /// Incremental sync since last sync.
    /// </summary>
    public const string Incremental = "incremental";

    public static readonly string[] All = [Notices, Returns, Full, Incremental];
}

/// <summary>
/// Sync operation status constants.
/// </summary>
public static class GstnSyncStatus
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

    public static readonly string[] All =
    [
        InProgress, Completed, CompletedWithWarnings, Failed, Cancelled
    ];
}

/// <summary>
/// What triggered the sync operation.
/// </summary>
public static class GstnSyncTrigger
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
    /// Triggered by webhook notification.
    /// </summary>
    public const string Webhook = "webhook";

    /// <summary>
    /// Retry after previous failure.
    /// </summary>
    public const string Retry = "retry";

    public static readonly string[] All =
    [
        Scheduled, Manual, Initial, Webhook, Retry
    ];
}
