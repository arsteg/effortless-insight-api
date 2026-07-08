using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.GstSync;

/// <summary>
/// Tracks reminders sent to users to sync their GSTINs.
/// </summary>
public class GstSyncReminder : BaseEntity
{
    /// <summary>
    /// The GST client connection this reminder is for.
    /// </summary>
    [Required]
    public Guid GstClientId { get; set; }

    [ForeignKey(nameof(GstClientId))]
    public GstClient GstClient { get; set; } = null!;

    /// <summary>
    /// Organization ID (denormalized for querying).
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// User to whom the reminder was sent.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Type of reminder.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ReminderType { get; set; } = null!;

    /// <summary>
    /// Channel through which reminder was sent.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Channel { get; set; } = null!;

    /// <summary>
    /// Current status of the reminder.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = GstSyncReminderStatus.Pending;

    /// <summary>
    /// When the reminder was sent.
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// When the user clicked/acted on the reminder.
    /// </summary>
    public DateTime? ClickedAt { get; set; }

    /// <summary>
    /// When the user dismissed the reminder.
    /// </summary>
    public DateTime? DismissedAt { get; set; }

    /// <summary>
    /// Content of the reminder message.
    /// </summary>
    public string? MessageContent { get; set; }
}

/// <summary>
/// Reminder type constants.
/// </summary>
public static class GstSyncReminderType
{
    /// <summary>
    /// Sync is due based on frequency.
    /// </summary>
    public const string SyncDue = "sync_due";

    /// <summary>
    /// Sync is overdue (missed scheduled sync).
    /// </summary>
    public const string SyncOverdue = "sync_overdue";

    /// <summary>
    /// Extension has been inactive.
    /// </summary>
    public const string ExtensionInactive = "extension_inactive";

    /// <summary>
    /// New notices are available on the portal.
    /// </summary>
    public const string NewNoticesAvailable = "new_notices_available";

    /// <summary>
    /// Connection is in error state.
    /// </summary>
    public const string ConnectionError = "connection_error";

    /// <summary>
    /// First-time setup reminder.
    /// </summary>
    public const string SetupReminder = "setup_reminder";

    public static readonly string[] All =
    [
        SyncDue, SyncOverdue, ExtensionInactive, NewNoticesAvailable, ConnectionError, SetupReminder
    ];

    public static bool IsValid(string type) => All.Contains(type);
}

/// <summary>
/// Reminder channel constants.
/// </summary>
public static class GstSyncReminderChannel
{
    public const string Email = "email";
    public const string InApp = "in_app";
    public const string Sms = "sms";
    public const string Push = "push";

    public static readonly string[] All =
    [
        Email, InApp, Sms, Push
    ];

    public static bool IsValid(string channel) => All.Contains(channel);
}

/// <summary>
/// Reminder status constants.
/// </summary>
public static class GstSyncReminderStatus
{
    /// <summary>
    /// Reminder is scheduled but not yet sent.
    /// </summary>
    public const string Pending = "pending";

    /// <summary>
    /// Reminder has been sent.
    /// </summary>
    public const string Sent = "sent";

    /// <summary>
    /// User clicked/acted on the reminder.
    /// </summary>
    public const string Clicked = "clicked";

    /// <summary>
    /// User dismissed the reminder.
    /// </summary>
    public const string Dismissed = "dismissed";

    /// <summary>
    /// Reminder failed to send.
    /// </summary>
    public const string Failed = "failed";

    public static readonly string[] All =
    [
        Pending, Sent, Clicked, Dismissed, Failed
    ];

    public static bool IsValid(string status) => All.Contains(status);
}
