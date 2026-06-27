using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a stage in a workflow template.
/// </summary>
public class WorkflowStage : BaseEntity
{
    [Required]
    public Guid WorkflowTemplateId { get; set; }
    public WorkflowTemplate WorkflowTemplate { get; set; } = null!;

    /// <summary>
    /// Unique key for this stage within the workflow (e.g., "intake", "analysis").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string StageKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(20)]
    public string StageType { get; set; } = WorkflowStageTypes.Intermediate;

    public int StageOrder { get; set; }

    /// <summary>
    /// Reference to a task template for auto-creating tasks when entering this stage.
    /// </summary>
    public Guid? TaskTemplateId { get; set; }

    /// <summary>
    /// Whether to automatically create a task when entering this stage.
    /// </summary>
    public bool AutoCreateTask { get; set; }

    /// <summary>
    /// Parallel branch identifier. Stages with the same branch ID execute in parallel.
    /// Null means sequential execution.
    /// </summary>
    [MaxLength(50)]
    public string? ParallelBranchId { get; set; }

    /// <summary>
    /// Whether this stage is a synchronization point where parallel branches must converge.
    /// </summary>
    public bool IsSynchronizationPoint { get; set; }

    /// <summary>
    /// For sync points: "all" = wait for all branches, "any" = continue when first completes.
    /// </summary>
    [MaxLength(10)]
    public string? JoinType { get; set; }

    /// <summary>
    /// Minimum number of branches that must complete for "any" join type.
    /// </summary>
    public int? MinBranchesToComplete { get; set; }

    /// <summary>
    /// SLA duration in hours. Null for end stages.
    /// </summary>
    public int? SlaHours { get; set; }

    /// <summary>
    /// Percentage of SLA at which to trigger warning (default 75%).
    /// </summary>
    public int SlaWarningPercent { get; set; } = 75;

    [MaxLength(7)]
    public string Color { get; set; } = "#6B7280";

    [MaxLength(50)]
    public string Icon { get; set; } = "circle";

    /// <summary>
    /// Stage keys that this stage can transition to.
    /// </summary>
    public List<string> AllowedTransitions { get; set; } = [];

    /// <summary>
    /// Actions to execute when entering this stage.
    /// </summary>
    public List<WorkflowAction> EntryActions { get; set; } = [];

    /// <summary>
    /// Actions to execute when leaving this stage.
    /// </summary>
    public List<WorkflowAction> ExitActions { get; set; } = [];

    /// <summary>
    /// Rules for automatic stage advancement.
    /// </summary>
    public List<AutoTransitionRule> AutoTransitionRules { get; set; } = [];

    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Workflow stage type constants.
/// </summary>
public static class WorkflowStageTypes
{
    public const string Start = "start";
    public const string Intermediate = "intermediate";
    public const string End = "end";
    public const string Pause = "pause";
    public const string Fork = "fork";
    public const string Join = "join";
    public const string Task = "task";

    public static readonly string[] All = [Start, Intermediate, End, Pause, Fork, Join, Task];

    public static bool IsValid(string type) => All.Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Join type constants for parallel workflow synchronization.
/// </summary>
public static class WorkflowJoinTypes
{
    /// <summary>
    /// Wait for all parallel branches to complete before proceeding.
    /// </summary>
    public const string All = "all";

    /// <summary>
    /// Proceed when any branch completes (or MinBranchesToComplete if specified).
    /// </summary>
    public const string Any = "any";

    public static readonly string[] ValidTypes = [All, Any];

    public static bool IsValid(string? type) => type == null || ValidTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents an action to be executed during workflow transitions.
/// </summary>
public class WorkflowAction
{
    /// <summary>
    /// Action type: triggerAI, notify, createTask, setField, logSubmission, updateMetrics
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Configuration for this action.
    /// </summary>
    public Dictionary<string, object> Config { get; set; } = [];
}

/// <summary>
/// Rule for automatic stage transitions.
/// </summary>
public class AutoTransitionRule
{
    public WorkflowCondition Condition { get; set; } = new();
    public string TargetStage { get; set; } = string.Empty;
    public int DelayMinutes { get; set; }
}

/// <summary>
/// Condition for workflow rules.
/// </summary>
public class WorkflowCondition
{
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Operator: eq, neq, gt, gte, lt, lte, in, contains
    /// </summary>
    public string Operator { get; set; } = string.Empty;

    public object? Value { get; set; }
}
