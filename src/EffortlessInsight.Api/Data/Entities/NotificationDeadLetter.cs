using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Dead letter queue for failed notification deliveries after max retries.
/// Used for manual review, reprocessing, or discarding failed notifications.
/// </summary>
public class NotificationDeadLetter : BaseEntity
{
    /// <summary>
    /// Reference to the original NotificationDelivery that failed
    /// </summary>
    [Required]
    public Guid OriginalDeliveryId { get; set; }

    /// <summary>
    /// Reference to the parent Notification
    /// </summary>
    [Required]
    public Guid NotificationId { get; set; }

    /// <summary>
    /// The channel that failed (email, sms, push, whatsapp)
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// Recipient identifier (email address, phone number, etc.)
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Recipient { get; set; } = string.Empty;

    /// <summary>
    /// The last error message from the delivery attempt
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Total number of delivery attempts made
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Timestamp of the first delivery attempt
    /// </summary>
    public DateTime FirstAttemptAt { get; set; }

    /// <summary>
    /// Timestamp of the last delivery attempt
    /// </summary>
    public DateTime LastAttemptAt { get; set; }

    /// <summary>
    /// Serialized notification content (JSON) for potential reprocessing
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string? Payload { get; set; }

    /// <summary>
    /// Whether this dead letter has been resolved
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Timestamp when the dead letter was resolved
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// How the dead letter was resolved: reprocessed, discarded, manual
    /// </summary>
    [MaxLength(20)]
    public string? Resolution { get; set; }

    /// <summary>
    /// User ID who resolved the dead letter (for audit)
    /// </summary>
    public Guid? ResolvedById { get; set; }

    /// <summary>
    /// Additional notes about the resolution
    /// </summary>
    public string? ResolutionNotes { get; set; }

    // Navigation properties
    [ForeignKey(nameof(NotificationId))]
    public Notification Notification { get; set; } = null!;

    [ForeignKey(nameof(OriginalDeliveryId))]
    public NotificationDelivery OriginalDelivery { get; set; } = null!;
}

/// <summary>
/// Dead letter resolution types
/// </summary>
public static class DeadLetterResolution
{
    public const string Reprocessed = "reprocessed";
    public const string Discarded = "discarded";
    public const string Manual = "manual";
}
