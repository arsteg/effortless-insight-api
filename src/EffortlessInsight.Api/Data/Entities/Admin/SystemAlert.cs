using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// System alerts for admin monitoring.
/// Tracks critical events, threshold breaches, and incidents.
/// </summary>
public class SystemAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Alert type: error, warning, info, critical
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string AlertType { get; set; } = "warning";

    /// <summary>
    /// Alert category: system, billing, ai, security, performance
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = "system";

    /// <summary>
    /// Alert source/component: api, database, redis, ai_service, etc.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Alert title/headline
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Additional data as JSON
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// Threshold that was breached (if applicable)
    /// </summary>
    [MaxLength(255)]
    public string? ThresholdInfo { get; set; }

    /// <summary>
    /// Current metric value (if applicable)
    /// </summary>
    public double? CurrentValue { get; set; }

    /// <summary>
    /// Threshold value (if applicable)
    /// </summary>
    public double? ThresholdValue { get; set; }

    /// <summary>
    /// Alert status: active, acknowledged, resolved, dismissed
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "active";

    /// <summary>
    /// Priority/severity: low, medium, high, critical
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = "medium";

    /// <summary>
    /// Number of occurrences (for deduplication)
    /// </summary>
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>
    /// First occurrence
    /// </summary>
    public DateTime FirstOccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last occurrence
    /// </summary>
    public DateTime LastOccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Admin who acknowledged the alert
    /// </summary>
    public Guid? AcknowledgedById { get; set; }

    [ForeignKey(nameof(AcknowledgedById))]
    public AdminUser? AcknowledgedBy { get; set; }

    /// <summary>
    /// When acknowledged
    /// </summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>
    /// Acknowledgment notes
    /// </summary>
    [MaxLength(500)]
    public string? AcknowledgmentNotes { get; set; }

    /// <summary>
    /// Admin who resolved the alert
    /// </summary>
    public Guid? ResolvedById { get; set; }

    [ForeignKey(nameof(ResolvedById))]
    public AdminUser? ResolvedBy { get; set; }

    /// <summary>
    /// When resolved
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Resolution notes
    /// </summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// Whether notifications were sent
    /// </summary>
    public bool NotificationsSent { get; set; }

    /// <summary>
    /// Who was notified (email addresses)
    /// </summary>
    public List<string>? NotifiedEmails { get; set; }

    /// <summary>
    /// Related incident/ticket ID
    /// </summary>
    [MaxLength(100)]
    public string? IncidentId { get; set; }

    /// <summary>
    /// Timestamps
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Alert type constants
/// </summary>
public static class AlertTypes
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}

/// <summary>
/// Alert category constants
/// </summary>
public static class AlertCategories
{
    public const string System = "system";
    public const string Billing = "billing";
    public const string AiService = "ai";
    public const string Security = "security";
    public const string Performance = "performance";
    public const string Compliance = "compliance";
}

/// <summary>
/// Alert status constants
/// </summary>
public static class AlertStatus
{
    public const string Active = "active";
    public const string Acknowledged = "acknowledged";
    public const string Resolved = "resolved";
    public const string Dismissed = "dismissed";
}

/// <summary>
/// Alert priority constants
/// </summary>
public static class AlertPriority
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

/// <summary>
/// Alert source constants
/// </summary>
public static class AlertSources
{
    public const string ApiGateway = "api_gateway";
    public const string MainApi = "main_api";
    public const string Database = "database";
    public const string Redis = "redis";
    public const string AiService = "ai_service";
    public const string S3Storage = "s3_storage";
    public const string PaymentGateway = "payment_gateway";
    public const string EmailService = "email_service";
    public const string BackgroundJobs = "background_jobs";
}
