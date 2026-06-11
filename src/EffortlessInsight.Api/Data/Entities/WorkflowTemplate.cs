using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Defines a reusable workflow template with stages and transitions.
/// </summary>
public class WorkflowTemplate : BaseEntity
{
    /// <summary>
    /// Organization that owns this template. Null for system templates.
    /// </summary>
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int Version { get; set; } = 1;

    /// <summary>
    /// System templates are read-only and available to all organizations.
    /// </summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Notice types this workflow applies to. ["*"] = all types.
    /// </summary>
    public List<string> ApplicableNoticeTypes { get; set; } = ["*"];

    public Guid? CreatedById { get; set; }
    public ApplicationUser? CreatedBy { get; set; }

    // Navigation properties
    public ICollection<WorkflowStage> Stages { get; set; } = [];
    public ICollection<WorkflowAssignmentRule> AssignmentRules { get; set; } = [];
    public ICollection<WorkflowEscalationRule> EscalationRules { get; set; } = [];
    public ICollection<NoticeWorkflowInstance> Instances { get; set; } = [];
}
