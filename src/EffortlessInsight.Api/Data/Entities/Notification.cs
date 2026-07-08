using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Notification priority levels determining delivery behavior
/// </summary>
public static class NotificationPriority
{
    public const string Critical = "critical";  // All channels, ignore quiet hours
    public const string High = "high";          // Email+SMS+Push, delay during quiet hours
    public const string Medium = "medium";      // Email+Push, held for digest during quiet hours
    public const string Low = "low";            // In-App only, batched into daily digest
}

/// <summary>
/// Notification categories for grouping and filtering
/// </summary>
public static class NotificationCategory
{
    public const string Deadline = "deadline";
    public const string Sla = "sla";
    public const string Notice = "notice";
    public const string Task = "task";
    public const string Collaboration = "collaboration";
    public const string Account = "account";
    public const string GstSync = "gst_sync";
}

/// <summary>
/// Notification type identifiers matching the blueprint specification
/// </summary>
public static class NotificationType
{
    // Deadline notifications
    public const string Deadline7Day = "deadline_7_day";
    public const string Deadline3Day = "deadline_3_day";
    public const string Deadline1Day = "deadline_1_day";
    public const string DeadlineToday = "deadline_today";
    public const string DeadlineMissed = "deadline_missed";

    // SLA notifications
    public const string SlaWarning = "sla_warning";
    public const string SlaCritical = "sla_critical";
    public const string SlaBreach = "sla_breach";

    // Notice notifications
    public const string NoticeUploaded = "notice_uploaded";
    public const string NoticeAnalyzed = "notice_analyzed";
    public const string NoticeHighRisk = "notice_high_risk";
    public const string NoticeAssigned = "notice_assigned";

    // Task notifications
    public const string TaskAssigned = "task_assigned";
    public const string TaskDueSoon = "task_due_soon";
    public const string TaskOverdue = "task_overdue";
    public const string TaskCompleted = "task_completed";

    // Collaboration notifications
    public const string CommentAdded = "comment_added";
    public const string UserMentioned = "user_mentioned";
    public const string DocumentRequested = "document_requested";
    public const string DocumentReceived = "document_received";

    // Account notifications
    public const string Welcome = "welcome";
    public const string PasswordReset = "password_reset";
    public const string LoginAlert = "login_alert";
    public const string SubscriptionExpiring = "subscription_expiring";

    // GST Sync notifications
    public const string GstSyncNoticesSynced = "gst_sync.notices_synced";
    public const string GstSyncDailyDigest = "gst_sync.daily_digest";
    public const string GstSyncFailed = "gst_sync.sync_failed";
    public const string GstSyncDueDateReminder = "gst_sync.due_date_reminder";
    public const string GstSyncDueDateOverdue = "gst_sync.due_date_overdue";
    public const string GstSyncExtensionDisconnected = "gst_sync.extension_disconnected";
    public const string GstSyncPaused = "gst_sync.sync_paused";
    public const string GstSyncImportCompleted = "gst_sync.import_completed";

    /// <summary>
    /// Get priority for a notification type
    /// </summary>
    public static string GetPriority(string type) => type switch
    {
        Deadline1Day or DeadlineToday or DeadlineMissed or NoticeHighRisk or
        SlaBreach or PasswordReset or GstSyncDueDateOverdue => NotificationPriority.Critical,

        Deadline3Day or SlaCritical or TaskOverdue or DocumentRequested or
        LoginAlert or SubscriptionExpiring or GstSyncFailed or
        GstSyncDueDateReminder => NotificationPriority.High,

        Deadline7Day or SlaWarning or NoticeUploaded or NoticeAnalyzed or
        NoticeAssigned or TaskAssigned or TaskDueSoon or UserMentioned or
        GstSyncNoticesSynced or GstSyncExtensionDisconnected or
        GstSyncPaused => NotificationPriority.Medium,

        _ => NotificationPriority.Low
    };

    /// <summary>
    /// Get category for a notification type
    /// </summary>
    public static string GetCategory(string type) => type switch
    {
        Deadline7Day or Deadline3Day or Deadline1Day or DeadlineToday or DeadlineMissed
            => NotificationCategory.Deadline,
        SlaWarning or SlaCritical or SlaBreach
            => NotificationCategory.Sla,
        NoticeUploaded or NoticeAnalyzed or NoticeHighRisk or NoticeAssigned
            => NotificationCategory.Notice,
        TaskAssigned or TaskDueSoon or TaskOverdue or TaskCompleted
            => NotificationCategory.Task,
        CommentAdded or UserMentioned or DocumentRequested or DocumentReceived
            => NotificationCategory.Collaboration,
        GstSyncNoticesSynced or GstSyncDailyDigest or GstSyncFailed or
        GstSyncDueDateReminder or GstSyncDueDateOverdue or GstSyncExtensionDisconnected or
        GstSyncPaused or GstSyncImportCompleted
            => NotificationCategory.GstSync,
        _ => NotificationCategory.Account
    };

    /// <summary>
    /// Get default channels for a notification type
    /// </summary>
    public static string[] GetDefaultChannels(string type) => type switch
    {
        // Critical - All channels
        Deadline1Day or DeadlineToday or DeadlineMissed or NoticeHighRisk or
        GstSyncDueDateOverdue
            => ["email", "sms", "push", "whatsapp", "inApp"],

        // High - Email, SMS, Push
        Deadline3Day or SlaCritical or SlaBreach or TaskOverdue or LoginAlert or
        GstSyncFailed or GstSyncDueDateReminder
            => ["email", "sms", "push", "inApp"],

        // High with WhatsApp
        DocumentRequested => ["email", "push", "whatsapp", "inApp"],

        // Medium - Email, Push
        Deadline7Day or SlaWarning or NoticeUploaded or NoticeAnalyzed or
        NoticeAssigned or TaskAssigned or UserMentioned or SubscriptionExpiring or
        GstSyncNoticesSynced or GstSyncExtensionDisconnected or GstSyncPaused
            => ["email", "push", "inApp"],

        // GST Sync - Daily digest (email only)
        GstSyncDailyDigest => ["email"],

        // Medium - Push only
        TaskDueSoon or DocumentReceived => ["push", "inApp"],

        // Low - In-App only
        TaskCompleted or CommentAdded or GstSyncImportCompleted => ["inApp"],

        // Account - Email (and SMS for password reset)
        Welcome => ["email"],
        PasswordReset => ["email", "sms"],

        _ => ["inApp"]
    };
}

/// <summary>
/// Core notification entity storing all notification records
/// </summary>
public class Notification : BaseEntity
{
    [Required]
    public Guid UserId { get; set; }

    public Guid? OrganizationId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = null!;

    [Required]
    [MaxLength(30)]
    public string Category { get; set; } = null!;

    [Required]
    [MaxLength(10)]
    public string Priority { get; set; } = NotificationPriority.Medium;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required]
    public string Body { get; set; } = null!;

    /// <summary>
    /// Additional data payload stored as JSONB (notice details, action URLs, etc.)
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime? ReadAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Reference to external entity (Notice, Task, Comment, etc.)
    /// </summary>
    public Guid? ReferenceId { get; set; }

    [MaxLength(50)]
    public string? ReferenceType { get; set; }

    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    [ForeignKey(nameof(OrganizationId))]
    public Organization? Organization { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
}

/// <summary>
/// Delivery status for each notification channel
/// </summary>
public static class DeliveryStatus
{
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Opened = "opened";
    public const string Clicked = "clicked";
    public const string Failed = "failed";
    public const string Bounced = "bounced";
}

/// <summary>
/// Notification channel identifiers
/// </summary>
public static class NotificationChannel
{
    public const string Email = "email";
    public const string Sms = "sms";
    public const string Push = "push";
    public const string WhatsApp = "whatsapp";
    public const string InApp = "inApp";

    public static readonly string[] All = [Email, Sms, Push, WhatsApp, InApp];
}

/// <summary>
/// Tracks delivery status per channel for each notification
/// </summary>
public class NotificationDelivery : BaseEntity
{
    [Required]
    public Guid NotificationId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Channel { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = DeliveryStatus.Pending;

    /// <summary>
    /// External provider's message ID (Resend, Twilio, Firebase)
    /// </summary>
    [MaxLength(200)]
    public string? ProviderMessageId { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public DateTime? OpenedAt { get; set; }

    public DateTime? ClickedAt { get; set; }

    public DateTime? FailedAt { get; set; }

    public string? FailureReason { get; set; }

    public int RetryCount { get; set; } = 0;

    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Provider-specific metadata (response codes, headers, etc.)
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Navigation
    [ForeignKey(nameof(NotificationId))]
    public Notification Notification { get; set; } = null!;
}

/// <summary>
/// User notification preferences for channels and types
/// </summary>
public class UserNotificationPreference : BaseEntity
{
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Channel settings: { email: { enabled: true, address: "..." }, sms: { enabled: true, phone: "..." } }
    /// </summary>
    public Dictionary<string, object> ChannelSettings { get; set; } = new();

    /// <summary>
    /// Quiet hours: { enabled: true, start: "22:00", end: "07:00", timezone: "Asia/Kolkata" }
    /// </summary>
    public Dictionary<string, object> QuietHours { get; set; } = new();

    /// <summary>
    /// Type preferences: { deadline_7_day: { email: true, sms: false, push: true }, ... }
    /// </summary>
    public Dictionary<string, object> TypePreferences { get; set; } = new();

    /// <summary>
    /// Digest settings: { daily: { enabled: true, time: "09:00" }, weekly: { enabled: false, day: 1 } }
    /// </summary>
    public Dictionary<string, object> DigestSettings { get; set; } = new();

    // Navigation
    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;
}

/// <summary>
/// Push notification device tokens (Firebase, APNs)
/// </summary>
public class PushToken : BaseEntity
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Token { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Platform { get; set; } = null!;  // web, android, ios

    /// <summary>
    /// Device info: { deviceId, model, os, appVersion }
    /// </summary>
    public Dictionary<string, object> DeviceInfo { get; set; } = new();

    public bool IsActive { get; set; } = true;

    public DateTime? LastUsedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;
}

/// <summary>
/// Notification templates for multi-channel, multi-language support
/// </summary>
public class NotificationTemplate : BaseEntity
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Channel { get; set; } = null!;

    [Required]
    [MaxLength(10)]
    public string Language { get; set; } = "en";

    public int Version { get; set; } = 1;

    [MaxLength(200)]
    public string? Subject { get; set; }  // For email

    [Required]
    public string Body { get; set; } = null!;

    /// <summary>
    /// Metadata: { providerId: "d-abc123", variables: ["user_name", "deadline"] }
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Scheduled notifications for future delivery
/// </summary>
public class ScheduledNotification : BaseEntity
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = null!;

    /// <summary>
    /// Notification data payload
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    [Required]
    public DateTime ScheduledFor { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";  // pending, sent, cancelled

    /// <summary>
    /// Reference to the sent notification (after delivery)
    /// </summary>
    public Guid? SentNotificationId { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    [ForeignKey(nameof(SentNotificationId))]
    public Notification? SentNotification { get; set; }
}

/// <summary>
/// Email unsubscribe records for compliance
/// </summary>
public class EmailUnsubscribe
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = null!;

    public Guid? UserId { get; set; }

    /// <summary>
    /// Specific notification type unsubscribed from (null = all)
    /// </summary>
    [MaxLength(50)]
    public string? NotificationType { get; set; }

    [MaxLength(50)]
    public string? Reason { get; set; }

    public DateTime UnsubscribedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }
}
