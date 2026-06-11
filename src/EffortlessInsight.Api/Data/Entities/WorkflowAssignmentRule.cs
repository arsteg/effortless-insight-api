using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Defines rules for automatic notice assignment in a workflow.
/// </summary>
public class WorkflowAssignmentRule : BaseEntity
{
    [Required]
    public Guid WorkflowTemplateId { get; set; }
    public WorkflowTemplate WorkflowTemplate { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Priority for rule evaluation. Lower number = higher priority.
    /// </summary>
    public int Priority { get; set; } = 500;

    /// <summary>
    /// Conditions that must be met for this rule to apply.
    /// </summary>
    public List<RuleCondition> Conditions { get; set; } = [];

    /// <summary>
    /// Actions to execute when rule matches.
    /// </summary>
    public List<RuleAction> Actions { get; set; } = [];

    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Condition for assignment rules.
/// </summary>
public class RuleCondition
{
    /// <summary>
    /// Field to evaluate: riskScore, noticeType, noticeCategory, totalDemand, priority, gstin, etc.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Operator: eq, neq, gt, gte, lt, lte, in, contains
    /// </summary>
    public string Operator { get; set; } = string.Empty;

    public object? Value { get; set; }
}

/// <summary>
/// Action to execute when assignment rule matches.
/// </summary>
public class RuleAction
{
    /// <summary>
    /// Action type: assign, notify, setField
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Target for the action.
    /// </summary>
    public AssignmentTarget? Target { get; set; }

    /// <summary>
    /// Additional configuration.
    /// </summary>
    public Dictionary<string, object>? Config { get; set; }
}

/// <summary>
/// Target for assignment actions.
/// </summary>
public class AssignmentTarget
{
    /// <summary>
    /// Type of target: user, role, roundRobin
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Value for the target (user ID, role name, etc.)
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Pool for round-robin assignment (e.g., "all_members")
    /// </summary>
    public string? Pool { get; set; }
}
