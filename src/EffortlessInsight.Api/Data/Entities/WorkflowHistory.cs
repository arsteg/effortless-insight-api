using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Audit trail for workflow actions and stage transitions.
/// </summary>
public class WorkflowHistory : BaseEntity
{
    [Required]
    public Guid WorkflowInstanceId { get; set; }
    public NoticeWorkflowInstance WorkflowInstance { get; set; } = null!;

    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    /// <summary>
    /// Type of history event.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Stage key before the event (for transitions).
    /// </summary>
    [MaxLength(50)]
    public string? FromStageKey { get; set; }

    /// <summary>
    /// Stage key after the event (for transitions).
    /// </summary>
    [MaxLength(50)]
    public string? ToStageKey { get; set; }

    /// <summary>
    /// User who triggered this event.
    /// </summary>
    public Guid? PerformedById { get; set; }
    public ApplicationUser? PerformedBy { get; set; }

    /// <summary>
    /// System or process that triggered this event (for automated actions).
    /// </summary>
    [MaxLength(100)]
    public string? PerformedBySystem { get; set; }

    /// <summary>
    /// Previous assignee (for reassignment events).
    /// </summary>
    public Guid? PreviousAssigneeId { get; set; }

    /// <summary>
    /// New assignee (for assignment events).
    /// </summary>
    public Guid? NewAssigneeId { get; set; }

    /// <summary>
    /// Human-readable description of the event.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Reason provided by user (for transitions, rejections, etc.).
    /// </summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    /// <summary>
    /// Time spent in previous stage (in minutes).
    /// </summary>
    public int? TimeInStageMinutes { get; set; }

    /// <summary>
    /// SLA status at time of event.
    /// </summary>
    [MaxLength(20)]
    public string? SlaStatusAtEvent { get; set; }

    /// <summary>
    /// Additional event data.
    /// </summary>
    public Dictionary<string, object>? EventData { get; set; }

    /// <summary>
    /// IP address of the user (for audit purposes).
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string (for audit purposes).
    /// </summary>
    [MaxLength(500)]
    public string? UserAgent { get; set; }
}

/// <summary>
/// Workflow history event type constants.
/// </summary>
public static class WorkflowHistoryEventTypes
{
    public const string WorkflowStarted = "workflow_started";
    public const string StageTransition = "stage_transition";
    public const string Assignment = "assignment";
    public const string Reassignment = "reassignment";
    public const string SlaWarning = "sla_warning";
    public const string SlaBreach = "sla_breach";
    public const string Escalation = "escalation";
    public const string CommentAdded = "comment_added";
    public const string AttachmentAdded = "attachment_added";
    public const string DeadlineExtended = "deadline_extended";
    public const string WorkflowPaused = "workflow_paused";
    public const string WorkflowResumed = "workflow_resumed";
    public const string WorkflowCompleted = "workflow_completed";
    public const string WorkflowCancelled = "workflow_cancelled";
    public const string ActionExecuted = "action_executed";
    public const string AiAnalysis = "ai_analysis";
    public const string NotificationSent = "notification_sent";

    public static readonly string[] All =
    [
        WorkflowStarted, StageTransition, Assignment, Reassignment,
        SlaWarning, SlaBreach, Escalation, CommentAdded, AttachmentAdded,
        DeadlineExtended, WorkflowPaused, WorkflowResumed, WorkflowCompleted,
        WorkflowCancelled, ActionExecuted, AiAnalysis, NotificationSent
    ];

    public static bool IsValid(string type) => All.Contains(type, StringComparer.OrdinalIgnoreCase);
}
