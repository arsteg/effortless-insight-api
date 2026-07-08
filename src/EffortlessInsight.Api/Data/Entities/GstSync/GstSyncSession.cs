using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.GstSync;

/// <summary>
/// Tracks each sync operation performed by Chrome Extension or Desktop Agent.
/// Provides detailed audit trail and performance metrics.
/// </summary>
public class GstSyncSession : BaseEntity
{
    /// <summary>
    /// The GST client connection this sync belongs to.
    /// </summary>
    [Required]
    public Guid GstClientId { get; set; }

    [ForeignKey(nameof(GstClientId))]
    public GstClient GstClient { get; set; } = null!;

    /// <summary>
    /// Organization ID for efficient querying (denormalized).
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// GSTIN being synced (denormalized for efficient querying).
    /// </summary>
    [Required]
    [MaxLength(15)]
    public string Gstin { get; set; } = null!;

    /// <summary>
    /// Source of this sync operation.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string SyncSource { get; set; } = null!;

    /// <summary>
    /// Version of the extension/agent that performed the sync.
    /// </summary>
    [MaxLength(20)]
    public string? SourceVersion { get; set; }

    /// <summary>
    /// Additional metadata about the sync source (browser info, OS, etc.).
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? SourceMetadata { get; set; }

    /// <summary>
    /// When the sync operation started.
    /// </summary>
    [Required]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the sync operation completed (null if still in progress).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration of the sync in milliseconds.
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Current status of the sync session.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = GstSyncSessionStatus.InProgress;

    /// <summary>
    /// Error message if sync failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code for programmatic error handling.
    /// </summary>
    [MaxLength(50)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Number of notices found during this sync.
    /// </summary>
    public int NoticesFound { get; set; }

    /// <summary>
    /// Number of new notices discovered (not seen before).
    /// </summary>
    public int NoticesNew { get; set; }

    /// <summary>
    /// Number of notices with updated data.
    /// </summary>
    public int NoticesUpdated { get; set; }

    /// <summary>
    /// Number of notices unchanged since last sync.
    /// </summary>
    public int NoticesUnchanged { get; set; }

    /// <summary>
    /// Number of PDFs successfully downloaded.
    /// </summary>
    public int PdfsDownloaded { get; set; }

    /// <summary>
    /// Number of PDF downloads that failed.
    /// </summary>
    public int PdfsFailed { get; set; }

    // Navigation property
    public ICollection<GstNoticeRaw> Notices { get; set; } = [];
}

/// <summary>
/// Status constants for GST sync sessions.
/// </summary>
public static class GstSyncSessionStatus
{
    /// <summary>
    /// Sync is currently in progress.
    /// </summary>
    public const string InProgress = "in_progress";

    /// <summary>
    /// Sync completed successfully.
    /// </summary>
    public const string Completed = "completed";

    /// <summary>
    /// Sync failed with error.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// Sync partially completed (some operations failed).
    /// </summary>
    public const string Partial = "partial";

    public static readonly string[] All =
    [
        InProgress, Completed, Failed, Partial
    ];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool IsTerminal(string status) => status is Completed or Failed or Partial;
}

/// <summary>
/// Sync source constants.
/// </summary>
public static class GstSyncSource
{
    public const string ChromeExtension = "chrome_extension";
    public const string DesktopAgent = "desktop_agent";
    public const string ManualUpload = "manual_upload";

    public static readonly string[] All =
    [
        ChromeExtension, DesktopAgent, ManualUpload
    ];

    public static bool IsValid(string source) => All.Contains(source);
}
