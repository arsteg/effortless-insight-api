using EffortlessInsight.Api.Features.Workflows.Dtos;

namespace EffortlessInsight.Api.Features.Workflows.Services;

/// <summary>
/// Core workflow engine service for managing notice workflows.
/// </summary>
public interface IWorkflowEngineService
{
    #region Workflow Instance Management

    /// <summary>
    /// Starts a workflow for a notice.
    /// </summary>
    Task<TransitionResult> StartWorkflowAsync(
        StartWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active workflow instance for a notice.
    /// </summary>
    Task<WorkflowInstanceDto?> GetWorkflowInstanceAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets workflow instance by ID.
    /// </summary>
    Task<WorkflowInstanceDto?> GetWorkflowInstanceByIdAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets workflow instances for multiple notices.
    /// </summary>
    Task<Dictionary<Guid, WorkflowInstanceSummaryDto>> GetWorkflowInstancesForNoticesAsync(
        IEnumerable<Guid> noticeIds,
        CancellationToken cancellationToken = default);

    #endregion

    #region Stage Transitions

    /// <summary>
    /// Transitions a workflow to a new stage.
    /// </summary>
    Task<TransitionResult> TransitionStageAsync(
        Guid noticeId,
        TransitionStageRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if a transition is allowed.
    /// </summary>
    Task<(bool IsValid, string? Error)> ValidateTransitionAsync(
        Guid noticeId,
        string targetStageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available transitions for the current stage.
    /// </summary>
    Task<List<WorkflowStageDto>> GetAvailableTransitionsAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs bulk stage transition for multiple notices.
    /// </summary>
    Task<BulkTransitionResult> BulkTransitionAsync(
        BulkTransitionRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Assignment

    /// <summary>
    /// Assigns a workflow to a user or role.
    /// </summary>
    Task<TransitionResult> AssignWorkflowAsync(
        Guid noticeId,
        AssignWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk assigns workflows to a user or role.
    /// </summary>
    Task<BulkTransitionResult> BulkAssignAsync(
        BulkAssignRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Workflow Control

    /// <summary>
    /// Pauses a workflow instance.
    /// </summary>
    Task<TransitionResult> PauseWorkflowAsync(
        Guid noticeId,
        PauseWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused workflow instance.
    /// </summary>
    Task<TransitionResult> ResumeWorkflowAsync(
        Guid noticeId,
        ResumeWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a workflow instance.
    /// </summary>
    Task<TransitionResult> CancelWorkflowAsync(
        Guid noticeId,
        string reason,
        Guid userId,
        CancellationToken cancellationToken = default);

    #endregion

    #region History and Progress

    /// <summary>
    /// Gets workflow history for a notice.
    /// </summary>
    Task<List<WorkflowHistoryDto>> GetWorkflowHistoryAsync(
        Guid noticeId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets workflow progress information.
    /// </summary>
    Task<WorkflowProgressDto?> GetWorkflowProgressAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    #endregion

    #region SLA Management

    /// <summary>
    /// Gets the SLA status for a workflow instance.
    /// </summary>
    Task<SlaStatusDto?> GetSlaStatusAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates SLA status for all active workflows.
    /// Called periodically by background job.
    /// </summary>
    Task UpdateSlaStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes SLA escalations for workflows at risk or breached.
    /// Called periodically by background job.
    /// </summary>
    Task ProcessEscalationsAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Parallel Execution

    /// <summary>
    /// Gets active stage instances for a workflow (supports parallel execution).
    /// </summary>
    Task<List<StageInstanceDto>> GetActiveStageInstancesAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a specific stage instance in a parallel workflow.
    /// Handles join logic when completing branches.
    /// </summary>
    Task<TransitionResult> CompleteStageInstanceAsync(
        Guid noticeId,
        Guid stageInstanceId,
        CompleteStageInstanceRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forks the workflow into parallel branches.
    /// </summary>
    Task<TransitionResult> ForkWorkflowAsync(
        Guid noticeId,
        ForkWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets parallel branch status for a workflow.
    /// </summary>
    Task<ParallelBranchStatusDto?> GetParallelBranchStatusAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Templates

    /// <summary>
    /// Gets available workflow templates for an organization.
    /// </summary>
    Task<List<WorkflowTemplateSummaryDto>> GetAvailableTemplatesAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a workflow template by ID.
    /// </summary>
    Task<WorkflowTemplateDto?> GetTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default workflow template.
    /// </summary>
    Task<WorkflowTemplateDto?> GetDefaultTemplateAsync(
        CancellationToken cancellationToken = default);

    #endregion
}
