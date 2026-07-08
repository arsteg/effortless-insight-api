using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.GstSync;

/// <summary>
/// Tracks which GSTINs are being monitored for GST notice sync via Chrome Extension or Desktop Agent.
/// This is part of the isolated GstSync module - separate from the GSP-based GSTN integration.
/// </summary>
public class GstClient : BaseEntity
{
    /// <summary>
    /// Organization that owns this GST client connection.
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// User who added this GSTIN for monitoring.
    /// </summary>
    [Required]
    public Guid CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public ApplicationUser CreatedByUser { get; set; } = null!;

    /// <summary>
    /// The GSTIN being monitored (15 characters).
    /// Format: 2 digits state code + 10 char PAN + 1 entity code + 1 Z + 1 checksum.
    /// </summary>
    [Required]
    [MaxLength(15)]
    [RegularExpression(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$",
        ErrorMessage = "Invalid GSTIN format")]
    public string Gstin { get; set; } = null!;

    /// <summary>
    /// Trade name associated with this GSTIN (from GST portal).
    /// </summary>
    [MaxLength(255)]
    public string? TradeName { get; set; }

    /// <summary>
    /// Legal name of the entity (from GST portal).
    /// </summary>
    [MaxLength(255)]
    public string? LegalName { get; set; }

    /// <summary>
    /// State code extracted from GSTIN (first 2 digits).
    /// </summary>
    [MaxLength(2)]
    public string? StateCode { get; set; }

    /// <summary>
    /// Whether automatic sync is enabled for this client.
    /// </summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>
    /// Hours between automatic syncs (1 to 168 hours / 1 week).
    /// </summary>
    [Range(1, 168)]
    public int SyncFrequencyHours { get; set; } = 6;

    /// <summary>
    /// Whether to automatically import synced notices to the main Notices module.
    /// </summary>
    public bool AutoImportToNotices { get; set; } = true;

    /// <summary>
    /// Current sync status.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = GstClientStatus.PendingFirstSync;

    /// <summary>
    /// Human-readable status message or error description.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// When the last sync operation was performed (regardless of success/failure).
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// Source of the last sync (chrome_extension, desktop_agent, manual).
    /// </summary>
    [MaxLength(50)]
    public string? LastSyncSource { get; set; }

    /// <summary>
    /// When the last successful sync occurred.
    /// </summary>
    public DateTime? LastSuccessfulSyncAt { get; set; }

    /// <summary>
    /// When the next sync is due (calculated from frequency).
    /// </summary>
    public DateTime? NextSyncDueAt { get; set; }

    /// <summary>
    /// Number of consecutive sync failures (resets to 0 on success).
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Total number of notices synced for this GSTIN.
    /// </summary>
    public int TotalNoticesSynced { get; set; }

    /// <summary>
    /// Total number of sync operations performed.
    /// </summary>
    public int TotalSyncsPerformed { get; set; }

    // Navigation properties
    public ICollection<GstSyncSession> SyncSessions { get; set; } = [];
    public ICollection<GstNoticeRaw> Notices { get; set; } = [];
    public ICollection<GstSyncReminder> Reminders { get; set; } = [];
}

/// <summary>
/// Status constants for GST client sync connections.
/// </summary>
public static class GstClientStatus
{
    /// <summary>
    /// Connection created but never synced.
    /// </summary>
    public const string PendingFirstSync = "pending_first_sync";

    /// <summary>
    /// Actively syncing with recent successful syncs.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// Sync paused by user.
    /// </summary>
    public const string Paused = "paused";

    /// <summary>
    /// In error state due to repeated failures.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// Disabled by user or system.
    /// </summary>
    public const string Disabled = "disabled";

    public static readonly string[] All =
    [
        PendingFirstSync, Active, Paused, Error, Disabled
    ];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool CanSync(string status) => status is Active or PendingFirstSync;
}
