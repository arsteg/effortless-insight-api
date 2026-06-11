using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Defines escalation rules for SLA breaches in a workflow.
/// </summary>
public class WorkflowEscalationRule : BaseEntity
{
    [Required]
    public Guid WorkflowTemplateId { get; set; }
    public WorkflowTemplate WorkflowTemplate { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Percentage of SLA at which to trigger this escalation (e.g., 75, 90, 100).
    /// </summary>
    public int TriggerPercent { get; set; }

    /// <summary>
    /// Actions to execute when escalation triggers.
    /// </summary>
    public List<EscalationAction> Actions { get; set; } = [];
}

/// <summary>
/// Action to execute during escalation.
/// </summary>
public class EscalationAction
{
    /// <summary>
    /// Action type: notify, flag, reassign
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Target for notification: assignee, manager, admin
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Notification template to use.
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// Value for flag actions.
    /// </summary>
    public string? Value { get; set; }
}
