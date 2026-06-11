using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Features.Workflows.Dtos;

#region Workflow Template DTOs

public record WorkflowTemplateDto
{
    public Guid Id { get; init; }
    public Guid? OrganizationId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Version { get; init; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public List<string> ApplicableNoticeTypes { get; init; } = [];
    public List<WorkflowStageDto> Stages { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public record WorkflowStageDto
{
    public Guid Id { get; init; }
    public string StageKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string StageType { get; init; } = string.Empty;
    public int StageOrder { get; init; }
    public int? SlaHours { get; init; }
    public int SlaWarningPercent { get; init; }
    public string Color { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public List<string> AllowedTransitions { get; init; } = [];
}

public record WorkflowTemplateSummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Version { get; init; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public int StageCount { get; init; }
    public int ActiveInstanceCount { get; init; }
}

#endregion

#region Workflow Instance DTOs

public record WorkflowInstanceDto
{
    public Guid Id { get; init; }
    public Guid NoticeId { get; init; }
    public Guid WorkflowTemplateId { get; init; }
    public string WorkflowTemplateName { get; init; } = string.Empty;
    public string CurrentStageKey { get; init; } = string.Empty;
    public string CurrentStageName { get; init; } = string.Empty;
    public DateTime StageEnteredAt { get; init; }
    public DateTime? SlaDeadline { get; init; }
    public string SlaStatus { get; init; } = string.Empty;
    public int SlaPercentConsumed { get; init; }
    public Guid? AssignedToId { get; init; }
    public string? AssignedToName { get; init; }
    public string? AssignedRole { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? CompletedAt { get; init; }
    public string? CompletionOutcome { get; init; }
    public int TotalTimeMinutes { get; init; }
    public int SlaBreachCount { get; init; }
    public int TransitionCount { get; init; }
    public List<string> AvailableTransitions { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public record WorkflowInstanceSummaryDto
{
    public Guid Id { get; init; }
    public Guid NoticeId { get; init; }
    public string CurrentStageKey { get; init; } = string.Empty;
    public string CurrentStageName { get; init; } = string.Empty;
    public string SlaStatus { get; init; } = string.Empty;
    public int SlaPercentConsumed { get; init; }
    public DateTime? SlaDeadline { get; init; }
    public string? AssignedToName { get; init; }
    public string Status { get; init; } = string.Empty;
}

#endregion

#region Workflow History DTOs

public record WorkflowHistoryDto
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? FromStageKey { get; init; }
    public string? ToStageKey { get; init; }
    public Guid? PerformedById { get; init; }
    public string? PerformedByName { get; init; }
    public string? PerformedBySystem { get; init; }
    public string? Description { get; init; }
    public string? Reason { get; init; }
    public int? TimeInStageMinutes { get; init; }
    public string? SlaStatusAtEvent { get; init; }
    public DateTime CreatedAt { get; init; }
}

#endregion

#region Request DTOs

public record StartWorkflowRequest
{
    public Guid NoticeId { get; init; }
    public Guid? WorkflowTemplateId { get; init; }
    public Guid? AssignToUserId { get; init; }
    public string? AssignToRole { get; init; }
}

public record TransitionStageRequest
{
    public string TargetStageKey { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record BulkTransitionRequest
{
    public List<Guid> NoticeIds { get; init; } = [];
    public string TargetStageKey { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

public record AssignWorkflowRequest
{
    public Guid? AssignToUserId { get; init; }
    public string? AssignToRole { get; init; }
    public string? Reason { get; init; }
}

public record BulkAssignRequest
{
    public List<Guid> NoticeIds { get; init; } = [];
    public Guid? AssignToUserId { get; init; }
    public string? AssignToRole { get; init; }
    public string? Reason { get; init; }
}

public record PauseWorkflowRequest
{
    public string Reason { get; init; } = string.Empty;
}

public record ResumeWorkflowRequest
{
    public string? Notes { get; init; }
}

#endregion

#region Response DTOs

public record TransitionResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public WorkflowInstanceDto? Instance { get; init; }
    public List<string>? Errors { get; init; }
}

public record BulkTransitionResult
{
    public int TotalRequested { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public List<BulkItemResult> Results { get; init; } = [];
}

public record BulkItemResult
{
    public Guid NoticeId { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
}

public record WorkflowStageInfo
{
    public string StageKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string StageType { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public int? SlaHours { get; init; }
    public bool IsCurrentStage { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime? EnteredAt { get; init; }
    public DateTime? ExitedAt { get; init; }
    public int? TimeInStageMinutes { get; init; }
}

public record WorkflowProgressDto
{
    public Guid NoticeId { get; init; }
    public Guid WorkflowInstanceId { get; init; }
    public string CurrentStageKey { get; init; } = string.Empty;
    public List<WorkflowStageInfo> Stages { get; init; } = [];
    public int CompletedStages { get; init; }
    public int TotalStages { get; init; }
    public decimal ProgressPercent { get; init; }
}

#endregion

#region SLA DTOs

public record SlaStatusDto
{
    public Guid WorkflowInstanceId { get; init; }
    public Guid NoticeId { get; init; }
    public string CurrentStageKey { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int PercentConsumed { get; init; }
    public DateTime? Deadline { get; init; }
    public int? HoursRemaining { get; init; }
    public int? MinutesRemaining { get; init; }
    public bool IsBreached { get; init; }
    public bool IsAtRisk { get; init; }
    public bool IsWarning { get; init; }
}

#endregion
