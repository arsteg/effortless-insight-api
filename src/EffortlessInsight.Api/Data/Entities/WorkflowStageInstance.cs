using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks an active stage within a workflow instance.
/// Supports parallel execution by allowing multiple active stage instances.
/// </summary>
public class WorkflowStageInstance : BaseEntity
{
    [Required]
    public Guid WorkflowInstanceId { get; set; }
    public NoticeWorkflowInstance WorkflowInstance { get; set; } = null!;

    [Required]
    public Guid StageId { get; set; }
    public WorkflowStage Stage { get; set; } = null!;

    /// <summary>
    /// Stage key for quick lookups.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string StageKey { get; set; } = string.Empty;

    /// <summary>
    /// Parallel branch this instance belongs to (if any).
    /// </summary>
    [MaxLength(50)]
    public string? BranchId { get; set; }

    /// <summary>
    /// Status of this stage instance.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = StageInstanceStatuses.Active;

    /// <summary>
    /// When the stage was entered.
    /// </summary>
    public DateTime EnteredAt { get; set; }

    /// <summary>
    /// When the stage was completed/exited.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// SLA deadline for this stage instance.
    /// </summary>
    public DateTime? SlaDeadline { get; set; }

    /// <summary>
    /// Current SLA status.
    /// </summary>
    [MaxLength(20)]
    public string SlaStatus { get; set; } = WorkflowSlaStatuses.OnTrack;

    /// <summary>
    /// Percentage of SLA consumed.
    /// </summary>
    public int SlaPercentConsumed { get; set; }

    /// <summary>
    /// User assigned to this stage instance.
    /// </summary>
    public Guid? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    /// <summary>
    /// Role assigned if no specific user.
    /// </summary>
    [MaxLength(100)]
    public string? AssignedRole { get; set; }

    /// <summary>
    /// Outcome when completed (e.g., "approved", "rejected", "completed").
    /// </summary>
    [MaxLength(50)]
    public string? Outcome { get; set; }

    /// <summary>
    /// Time spent in this stage (in minutes).
    /// </summary>
    public int TimeSpentMinutes { get; set; }

    /// <summary>
    /// Custom metadata for this stage instance.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Navigation to the workflow stage definition.
    /// </summary>
    public WorkflowStage? WorkflowStage { get; set; }
}

/// <summary>
/// Stage instance status constants.
/// </summary>
public static class StageInstanceStatuses
{
    /// <summary>
    /// Stage is currently active and being worked on.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// Stage is paused (e.g., waiting for external input).
    /// </summary>
    public const string Paused = "paused";

    /// <summary>
    /// Stage completed successfully.
    /// </summary>
    public const string Completed = "completed";

    /// <summary>
    /// Stage was skipped (e.g., condition not met).
    /// </summary>
    public const string Skipped = "skipped";

    /// <summary>
    /// Stage was cancelled (e.g., another branch completed first in "any" join).
    /// </summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Stage is waiting at a synchronization point.
    /// </summary>
    public const string WaitingAtJoin = "waiting_at_join";

    public static readonly string[] All = [Active, Paused, Completed, Skipped, Cancelled, WaitingAtJoin];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.OrdinalIgnoreCase);
}
