using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Stores webhook events for idempotent processing and audit.
/// </summary>
public class WebhookEvent : BaseEntity
{
    /// <summary>
    /// Payment provider: razorpay
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Provider { get; set; } = "razorpay";

    /// <summary>
    /// Unique event ID from the provider.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Event type (e.g., payment.captured, subscription.activated).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Processing status: pending, processing, processed, failed
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = WebhookEventStatus.Pending;

    /// <summary>
    /// Raw payload from the webhook.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Signature from the webhook headers.
    /// </summary>
    [MaxLength(200)]
    public string? Signature { get; set; }

    /// <summary>
    /// Related entity ID (e.g., payment ID, subscription ID).
    /// </summary>
    [MaxLength(100)]
    public string? RelatedEntityId { get; set; }

    /// <summary>
    /// Related entity type.
    /// </summary>
    [MaxLength(50)]
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// When processing started.
    /// </summary>
    public DateTime? ProcessingStartedAt { get; set; }

    /// <summary>
    /// When processing completed.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of processing attempts.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// When the last processing attempt was made.
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// IP address the webhook was received from.
    /// </summary>
    [MaxLength(50)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Processing result details.
    /// </summary>
    public Dictionary<string, object>? ProcessingResult { get; set; }
}

/// <summary>
/// Webhook event status constants.
/// </summary>
public static class WebhookEventStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Processed = "processed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string DeadLetter = "dead_letter";
}
