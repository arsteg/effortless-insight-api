using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks processed WhatsApp webhook events for idempotency.
/// Prevents duplicate processing of webhook payloads.
/// </summary>
public class WhatsAppWebhookEvent
{
    /// <summary>
    /// Hash of the webhook payload for idempotency check.
    /// </summary>
    [Key]
    [MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>
    /// Entry ID from the webhook payload (entry[].id).
    /// </summary>
    [MaxLength(100)]
    public string? EntryId { get; set; }

    /// <summary>
    /// Type of event: message, status, error.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// When the webhook was received.
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the event was processed.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Processing result: success, error, skipped.
    /// </summary>
    [MaxLength(20)]
    public string? ProcessingResult { get; set; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
}
