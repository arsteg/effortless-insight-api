using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Support ticket status values
/// </summary>
public static class SupportTicketStatus
{
    public const string Open = "open";                // Awaiting first/next support reply
    public const string InProgress = "in_progress";   // Support is working on it / has replied
    public const string Resolved = "resolved";        // Support marked as resolved (customer can reopen by replying)
    public const string Closed = "closed";            // Terminal; no further replies expected

    public static readonly string[] All = [Open, InProgress, Resolved, Closed];
}

/// <summary>
/// Support ticket categories for triage
/// </summary>
public static class SupportTicketCategory
{
    public const string Question = "question";
    public const string Problem = "problem";
    public const string Billing = "billing";
    public const string FeatureRequest = "feature_request";
    public const string Other = "other";

    public static readonly string[] All = [Question, Problem, Billing, FeatureRequest, Other];
}

/// <summary>
/// In-app customer support ticket. Support is deliberately an in-app channel
/// (no support@ mailbox exists) — customers raise and follow tickets here,
/// admins reply via the admin endpoints.
/// </summary>
public class SupportTicket : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }

    [Required]
    public Guid CreatedByUserId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Subject { get; set; } = null!;

    [Required]
    [MaxLength(30)]
    public string Category { get; set; } = SupportTicketCategory.Question;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = SupportTicketStatus.Open;

    /// <summary>
    /// Timestamp of the most recent message (customer or support), for sorting.
    /// </summary>
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    [ForeignKey(nameof(CreatedByUserId))]
    public ApplicationUser CreatedBy { get; set; } = null!;

    public ICollection<SupportTicketMessage> Messages { get; set; } = new List<SupportTicketMessage>();
}

/// <summary>
/// A single message in a support ticket thread. Customer messages carry
/// SenderUserId; support replies have IsFromSupport = true and a display name.
/// </summary>
public class SupportTicketMessage : BaseEntity
{
    [Required]
    public Guid TicketId { get; set; }

    /// <summary>
    /// The customer user who wrote the message; null for support replies.
    /// </summary>
    public Guid? SenderUserId { get; set; }

    public bool IsFromSupport { get; set; }

    /// <summary>
    /// Display name shown in the thread (customer name or "EffortlessInsight Support").
    /// </summary>
    [MaxLength(100)]
    public string? SenderName { get; set; }

    [Required]
    public string Body { get; set; } = null!;

    // Navigation properties
    [ForeignKey(nameof(TicketId))]
    public SupportTicket Ticket { get; set; } = null!;

    [ForeignKey(nameof(SenderUserId))]
    public ApplicationUser? SenderUser { get; set; }
}
