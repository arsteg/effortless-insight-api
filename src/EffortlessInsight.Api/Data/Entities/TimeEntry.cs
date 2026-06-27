using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a time entry for tracking time spent on a task.
/// Supports both manual entry and timer-based tracking.
/// </summary>
public class TimeEntry : BaseEntity
{
    /// <summary>
    /// The task this time entry belongs to.
    /// </summary>
    [Required]
    public Guid TaskId { get; set; }
    public NoticeTask Task { get; set; } = null!;

    /// <summary>
    /// The user who logged this time entry.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// The date the work was performed.
    /// </summary>
    [Required]
    public DateOnly Date { get; set; }

    /// <summary>
    /// Number of hours worked (can be fractional, e.g., 1.5 for 1h 30m).
    /// </summary>
    [Required]
    public decimal Hours { get; set; }

    /// <summary>
    /// Optional description of the work performed.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this time entry is billable.
    /// </summary>
    public bool IsBillable { get; set; } = true;

    /// <summary>
    /// Start time for timer-based entries.
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// End time for timer-based entries.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Whether the timer is currently running (StartTime set but EndTime not set).
    /// </summary>
    public bool IsTimerRunning => StartTime.HasValue && !EndTime.HasValue;
}
