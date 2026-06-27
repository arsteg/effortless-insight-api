using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Defines an approval workflow chain with multiple steps.
/// </summary>
public class ApprovalChain : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// When this chain should be triggered (e.g., "on_response_submit", "on_status_change").
    /// </summary>
    [MaxLength(50)]
    public string? TriggerEvent { get; set; }

    /// <summary>
    /// Conditions for when this chain applies (JSON format).
    /// Example: {"notice_category": "demand", "amount_gte": 100000}
    /// </summary>
    public Dictionary<string, object>? TriggerConditions { get; set; }

    /// <summary>
    /// Whether this chain is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether to allow parallel approvals (all approvers at once) vs sequential.
    /// </summary>
    public bool IsParallel { get; set; }

    /// <summary>
    /// For parallel approvals, minimum number of approvals required.
    /// </summary>
    public int? MinApprovalsRequired { get; set; }

    /// <summary>
    /// Default timeout in hours for each step before escalation.
    /// </summary>
    public int? DefaultTimeoutHours { get; set; }

    // Navigation
    public ICollection<ApprovalStep> Steps { get; set; } = [];
    public ICollection<ApprovalRequest> Requests { get; set; } = [];
}

/// <summary>
/// Individual step in an approval chain.
/// </summary>
public class ApprovalStep : BaseEntity
{
    [Required]
    public Guid ApprovalChainId { get; set; }
    public ApprovalChain ApprovalChain { get; set; } = null!;

    /// <summary>
    /// Order of this step in the chain (1-based).
    /// </summary>
    [Required]
    public int StepOrder { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of approver: "user", "role", "manager", "department_head".
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string ApproverType { get; set; } = "user";

    /// <summary>
    /// Specific user ID if ApproverType is "user".
    /// </summary>
    public Guid? ApproverId { get; set; }
    public ApplicationUser? Approver { get; set; }

    /// <summary>
    /// Role name if ApproverType is "role".
    /// </summary>
    [MaxLength(50)]
    public string? ApproverRole { get; set; }

    /// <summary>
    /// Whether this step can be skipped if conditions aren't met.
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>
    /// Conditions for when this step is required (JSON).
    /// </summary>
    public Dictionary<string, object>? Conditions { get; set; }

    /// <summary>
    /// Timeout in hours for this specific step (overrides chain default).
    /// </summary>
    public int? TimeoutHours { get; set; }

    /// <summary>
    /// User to escalate to if timeout occurs.
    /// </summary>
    public Guid? EscalationUserId { get; set; }
    public ApplicationUser? EscalationUser { get; set; }

    /// <summary>
    /// Whether delegation is allowed for this step.
    /// </summary>
    public bool AllowDelegation { get; set; } = true;

    /// <summary>
    /// Instructions for the approver.
    /// </summary>
    [MaxLength(1000)]
    public string? Instructions { get; set; }
}

/// <summary>
/// Tracks an approval request for a specific notice.
/// </summary>
public class ApprovalRequest : BaseEntity
{
    [Required]
    public Guid ApprovalChainId { get; set; }
    public ApprovalChain ApprovalChain { get; set; } = null!;

    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    /// <summary>
    /// Optional: specific response being approved.
    /// </summary>
    public Guid? ResponseId { get; set; }
    public NoticeResponse? Response { get; set; }

    /// <summary>
    /// User who initiated the approval request.
    /// </summary>
    [Required]
    public Guid RequestedById { get; set; }
    public ApplicationUser RequestedBy { get; set; } = null!;

    /// <summary>
    /// Current step in the approval chain.
    /// </summary>
    public int CurrentStep { get; set; } = 1;

    /// <summary>
    /// Overall status: "pending", "approved", "rejected", "cancelled", "expired".
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>
    /// When the current step times out.
    /// </summary>
    public DateTime? CurrentStepDeadline { get; set; }

    /// <summary>
    /// When the request was completed (approved/rejected).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Additional context for the approval request.
    /// </summary>
    public string? RequestNotes { get; set; }

    /// <summary>
    /// Metadata about the request.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    // Navigation
    public ICollection<ApprovalAction> Actions { get; set; } = [];
}

/// <summary>
/// Records an approval/rejection/delegation action.
/// </summary>
public class ApprovalAction : BaseEntity
{
    [Required]
    public Guid ApprovalRequestId { get; set; }
    public ApprovalRequest ApprovalRequest { get; set; } = null!;

    [Required]
    public Guid ApprovalStepId { get; set; }
    public ApprovalStep ApprovalStep { get; set; } = null!;

    /// <summary>
    /// User who performed the action.
    /// </summary>
    [Required]
    public Guid ActorId { get; set; }
    public ApplicationUser Actor { get; set; } = null!;

    /// <summary>
    /// Type of action: "approved", "rejected", "delegated", "escalated", "recalled".
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Comments provided with the action.
    /// </summary>
    public string? Comments { get; set; }

    /// <summary>
    /// For delegation: the user delegated to.
    /// </summary>
    public Guid? DelegatedToId { get; set; }
    public ApplicationUser? DelegatedTo { get; set; }

    /// <summary>
    /// For delegation: reason for delegating.
    /// </summary>
    [MaxLength(500)]
    public string? DelegationReason { get; set; }

    /// <summary>
    /// Whether this was an automatic action (e.g., escalation).
    /// </summary>
    public bool IsAutomatic { get; set; }

    /// <summary>
    /// IP address of the actor.
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }
}

/// <summary>
/// Approval status constants.
/// </summary>
public static class ApprovalStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
}

/// <summary>
/// Approval action type constants.
/// </summary>
public static class ApprovalActionType
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Delegated = "delegated";
    public const string Escalated = "escalated";
    public const string Recalled = "recalled";
}

/// <summary>
/// Approver type constants.
/// </summary>
public static class ApproverType
{
    public const string User = "user";
    public const string Role = "role";
    public const string Manager = "manager";
    public const string DepartmentHead = "department_head";
}
