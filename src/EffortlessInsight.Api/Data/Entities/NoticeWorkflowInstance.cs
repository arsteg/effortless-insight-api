using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Links a notice to an active workflow instance and tracks its progress.
/// </summary>
public class NoticeWorkflowInstance : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    public Guid WorkflowTemplateId { get; set; }
    public WorkflowTemplate WorkflowTemplate { get; set; } = null!;

    /// <summary>
    /// Current stage key within the workflow.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string CurrentStageKey { get; set; } = string.Empty;

    public Guid? CurrentStageId { get; set; }
    public WorkflowStage? CurrentStage { get; set; }

    /// <summary>
    /// When the notice entered the current stage.
    /// </summary>
    public DateTime StageEnteredAt { get; set; }

    /// <summary>
    /// Calculated SLA deadline for current stage.
    /// </summary>
    public DateTime? SlaDeadline { get; set; }

    /// <summary>
    /// Current SLA status.
    /// </summary>
    [MaxLength(20)]
    public string SlaStatus { get; set; } = WorkflowSlaStatuses.OnTrack;

    /// <summary>
    /// Percentage of SLA consumed (0-100+).
    /// </summary>
    public int SlaPercentConsumed { get; set; }

    /// <summary>
    /// User currently assigned to this workflow instance.
    /// </summary>
    public Guid? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    /// <summary>
    /// Role or team assigned if no specific user.
    /// </summary>
    [MaxLength(100)]
    public string? AssignedRole { get; set; }

    /// <summary>
    /// Previous assignee for reassignment tracking.
    /// </summary>
    public Guid? PreviousAssigneeId { get; set; }
    public ApplicationUser? PreviousAssignee { get; set; }

    /// <summary>
    /// Overall workflow status.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = WorkflowInstanceStatuses.Active;

    /// <summary>
    /// When the workflow was completed or cancelled.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Final outcome of the workflow.
    /// </summary>
    [MaxLength(50)]
    public string? CompletionOutcome { get; set; }

    /// <summary>
    /// Total time spent in workflow (in minutes).
    /// </summary>
    public int TotalTimeMinutes { get; set; }

    /// <summary>
    /// Number of times SLA was breached across all stages.
    /// </summary>
    public int SlaBreachCount { get; set; }

    /// <summary>
    /// Number of stage transitions.
    /// </summary>
    public int TransitionCount { get; set; }

    /// <summary>
    /// Custom metadata for this workflow instance.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Version of the workflow template used when this instance was created.
    /// </summary>
    public int TemplateVersionUsed { get; set; }

    /// <summary>
    /// Whether this workflow instance has parallel stages active.
    /// </summary>
    public bool HasParallelStages { get; set; }

    /// <summary>
    /// Number of currently active parallel branches.
    /// </summary>
    public int ActiveBranchCount { get; set; }

    // Navigation properties
    public ICollection<WorkflowHistory> History { get; set; } = [];

    /// <summary>
    /// Active stage instances (supports parallel execution).
    /// </summary>
    public ICollection<WorkflowStageInstance> StageInstances { get; set; } = [];
}

/// <summary>
/// SLA status constants.
/// </summary>
public static class WorkflowSlaStatuses
{
    public const string OnTrack = "on_track";
    public const string Warning = "warning";
    public const string AtRisk = "at_risk";
    public const string Breached = "breached";
    public const string Paused = "paused";

    public static readonly string[] All = [OnTrack, Warning, AtRisk, Breached, Paused];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Workflow instance status constants.
/// </summary>
public static class WorkflowInstanceStatuses
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = [Active, Paused, Completed, Cancelled];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.OrdinalIgnoreCase);
}
