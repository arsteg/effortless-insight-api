using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Multi-channel unsubscribe records for email, SMS, WhatsApp, and push notifications.
/// Supports both global unsubscribe (all notifications) and category-specific unsubscribe.
/// </summary>
public class ChannelUnsubscribe : BaseEntity
{
    /// <summary>
    /// User who unsubscribed
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Navigation property to user
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Channel that was unsubscribed: email, sms, whatsapp, push
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// Optional category filter - null means all notifications for this channel,
    /// otherwise specific category (deadline, sla, notice, task, collaboration, account)
    /// </summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>
    /// Optional notification type filter - more granular than category
    /// e.g., "deadline_7_day", "sla_warning", etc.
    /// </summary>
    [MaxLength(50)]
    public string? NotificationType { get; set; }

    /// <summary>
    /// When the unsubscribe occurred
    /// </summary>
    public DateTime UnsubscribedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional reason provided by user for unsubscribing
    /// </summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Source of the unsubscribe: user_request, webhook, admin, spam_report
    /// </summary>
    [MaxLength(30)]
    public string Source { get; set; } = "user_request";

    /// <summary>
    /// If unsubscribed via webhook, store the external reference
    /// </summary>
    [MaxLength(255)]
    public string? ExternalReference { get; set; }
}

/// <summary>
/// Unsubscribe source types
/// </summary>
public static class UnsubscribeSource
{
    public const string UserRequest = "user_request";
    public const string Webhook = "webhook";
    public const string Admin = "admin";
    public const string SpamReport = "spam_report";
    public const string Bounce = "bounce";
}
