using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a reminder for a task that will be sent a certain number of days before the due date.
/// </summary>
public class TaskReminder : BaseEntity
{
    /// <summary>
    /// The task this reminder is for.
    /// </summary>
    [Required]
    public Guid TaskId { get; set; }
    public NoticeTask Task { get; set; } = null!;

    /// <summary>
    /// Number of days before the due date to send the reminder.
    /// </summary>
    [Required]
    public int DaysBeforeDue { get; set; }

    /// <summary>
    /// Whether this reminder has been sent.
    /// </summary>
    public bool IsSent { get; set; }

    /// <summary>
    /// When the reminder was sent.
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// User who created this reminder.
    /// </summary>
    public Guid? CreatedById { get; set; }
    public ApplicationUser? CreatedBy { get; set; }
}
