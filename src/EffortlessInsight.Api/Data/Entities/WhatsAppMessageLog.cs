using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Audit log for all WhatsApp messages (inbound and outbound).
/// Content is sanitized to avoid storing sensitive PII.
/// </summary>
public class WhatsAppMessageLog : BaseEntity
{
    /// <summary>
    /// Linked user ID if known.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Organization ID if known (for analytics).
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// Phone number (masked for privacy: +91****3210).
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Full phone number for retry (encrypted or stored only for retryable messages).
    /// </summary>
    [MaxLength(20)]
    public string? FullPhoneNumber { get; set; }

    /// <summary>
    /// Direction: inbound or outbound.
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string Direction { get; set; } = string.Empty;

    /// <summary>
    /// Message type: text, template, interactive, image, etc.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string MessageType { get; set; } = string.Empty;

    /// <summary>
    /// WhatsApp Message ID (wamid.xxx).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string WamId { get; set; } = string.Empty;

    /// <summary>
    /// Sanitized message content (no PII).
    /// </summary>
    [MaxLength(1000)]
    public string? Content { get; set; }

    /// <summary>
    /// Parsed command if any (status, notices, etc.).
    /// </summary>
    [MaxLength(50)]
    public string? Command { get; set; }

    /// <summary>
    /// Template name used for outbound messages.
    /// </summary>
    [MaxLength(100)]
    public string? TemplateName { get; set; }

    /// <summary>
    /// Template language code.
    /// </summary>
    [MaxLength(10)]
    public string? TemplateLanguage { get; set; }

    /// <summary>
    /// Template parameters stored as JSON for retry capability.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<string>? TemplateParameters { get; set; }

    /// <summary>
    /// Message status: sent, delivered, read, failed.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "sent";

    /// <summary>
    /// Error code if failed.
    /// </summary>
    [MaxLength(50)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Processing time in milliseconds.
    /// </summary>
    public int? ProcessingTimeMs { get; set; }

    /// <summary>
    /// When message was delivered.
    /// </summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>
    /// When message was read.
    /// </summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// Retry count for failed messages.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum retry attempts allowed.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Last retry timestamp.
    /// </summary>
    public DateTime? LastRetryAt { get; set; }

    /// <summary>
    /// Next retry scheduled time.
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Whether this message can be retried.
    /// </summary>
    public bool IsRetryable { get; set; } = true;

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    [MaxLength(50)]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Reference type (Notice, Task, etc.) for context.
    /// </summary>
    [MaxLength(50)]
    public string? ReferenceType { get; set; }

    /// <summary>
    /// Reference ID for context (NoticeId, TaskId, etc.).
    /// </summary>
    public Guid? ReferenceId { get; set; }

    // Navigation properties
    public ApplicationUser? User { get; set; }
    public Organization? Organization { get; set; }
}

/// <summary>
/// Message direction constants.
/// </summary>
public static class WhatsAppMessageDirection
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
}

/// <summary>
/// Message status constants.
/// </summary>
public static class WhatsAppMessageStatus
{
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Failed = "failed";
    public const string Pending = "pending";
}
