using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.GstSync;

/// <summary>
/// Tracks Chrome extension and Desktop Agent activity and errors for monitoring and debugging.
/// </summary>
public class GstExtensionEvent : BaseEntity
{
    /// <summary>
    /// Organization ID (may be null for pre-auth events).
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// User ID (may be null for pre-auth events).
    /// </summary>
    public Guid? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    /// <summary>
    /// Type of event.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = null!;

    /// <summary>
    /// Additional event data as JSON.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? EventData { get; set; }

    /// <summary>
    /// Version of the extension/agent.
    /// </summary>
    [MaxLength(20)]
    public string? ExtensionVersion { get; set; }

    /// <summary>
    /// Browser information.
    /// </summary>
    [MaxLength(255)]
    public string? BrowserInfo { get; set; }

    /// <summary>
    /// Error type if this is an error event.
    /// </summary>
    [MaxLength(100)]
    public string? ErrorType { get; set; }

    /// <summary>
    /// Error message if this is an error event.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Stack trace if this is an error event.
    /// </summary>
    public string? ErrorStack { get; set; }

    /// <summary>
    /// Page URL where the event/error occurred.
    /// </summary>
    public string? PageUrl { get; set; }
}

/// <summary>
/// Extension event type constants.
/// </summary>
public static class GstExtensionEventType
{
    /// <summary>
    /// Extension was installed.
    /// </summary>
    public const string Installed = "installed";

    /// <summary>
    /// Extension was uninstalled.
    /// </summary>
    public const string Uninstalled = "uninstalled";

    /// <summary>
    /// Extension was updated to new version.
    /// </summary>
    public const string Updated = "updated";

    /// <summary>
    /// User logged into the extension.
    /// </summary>
    public const string Login = "login";

    /// <summary>
    /// User logged out of the extension.
    /// </summary>
    public const string Logout = "logout";

    /// <summary>
    /// GST portal login detected.
    /// </summary>
    public const string GstLoginDetected = "gst_login_detected";

    /// <summary>
    /// GST portal logout detected.
    /// </summary>
    public const string GstLogoutDetected = "gst_logout_detected";

    /// <summary>
    /// Notices page visited.
    /// </summary>
    public const string NoticesPageVisited = "notices_page_visited";

    /// <summary>
    /// Notices were captured from the page.
    /// </summary>
    public const string NoticesCaptured = "notices_captured";

    /// <summary>
    /// Sync to backend started.
    /// </summary>
    public const string SyncStarted = "sync_started";

    /// <summary>
    /// Sync to backend completed.
    /// </summary>
    public const string SyncCompleted = "sync_completed";

    /// <summary>
    /// Sync to backend failed.
    /// </summary>
    public const string SyncFailed = "sync_failed";

    /// <summary>
    /// PDF download started.
    /// </summary>
    public const string PdfDownloadStarted = "pdf_download_started";

    /// <summary>
    /// PDF download completed.
    /// </summary>
    public const string PdfDownloadCompleted = "pdf_download_completed";

    /// <summary>
    /// PDF download failed.
    /// </summary>
    public const string PdfDownloadFailed = "pdf_download_failed";

    /// <summary>
    /// An error occurred.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// Configuration was updated.
    /// </summary>
    public const string ConfigUpdated = "config_updated";

    /// <summary>
    /// Extension heartbeat (periodic check-in).
    /// </summary>
    public const string Heartbeat = "heartbeat";

    public static readonly string[] All =
    [
        Installed, Uninstalled, Updated, Login, Logout,
        GstLoginDetected, GstLogoutDetected,
        NoticesPageVisited, NoticesCaptured,
        SyncStarted, SyncCompleted, SyncFailed,
        PdfDownloadStarted, PdfDownloadCompleted, PdfDownloadFailed,
        Error, ConfigUpdated, Heartbeat
    ];

    public static bool IsValid(string eventType) => All.Contains(eventType);
}
