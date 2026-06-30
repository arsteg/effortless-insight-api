using EffortlessInsight.Api.Features.Workflows.Dtos;
using EffortlessInsight.Api.Features.Workflows.Services;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for workflow management.
/// Exposes the workflow engine for stage transitions, parallel execution, and workflow control.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowEngineService _workflowEngine;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(
        IWorkflowEngineService workflowEngine,
        ICurrentOrganizationService orgService,
        ILogger<WorkflowsController> logger)
    {
        _workflowEngine = workflowEngine;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);

    // ==========================================================================
    // Workflow Instance Management
    // ==========================================================================

    /// <summary>
    /// Start a workflow for a notice.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartWorkflow(
        [FromBody] StartWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetUserId();
            _logger.LogInformation("Starting workflow for notice {NoticeId} by user {UserId}", request.NoticeId, userId);

            var result = await _workflowEngine.StartWorkflowAsync(request, userId, cancellationToken);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow for notice {NoticeId}", request.NoticeId);
            return StatusCode(500, new TransitionResult
            {
                Success = false,
                Message = "Internal server error",
                Errors = [ex.Message]
            });
        }
    }

    /// <summary>
    /// Get workflow instance for a notice.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}")]
    [ProducesResponseType(typeof(WorkflowInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkflowInstance(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        var instance = await _workflowEngine.GetWorkflowInstanceAsync(noticeId, cancellationToken);

        if (instance == null)
        {
            return NotFound(new { error = "No workflow found for this notice" });
        }

        return Ok(instance);
    }

    /// <summary>
    /// Get workflow instance by ID.
    /// </summary>
    [HttpGet("instances/{instanceId:guid}")]
    [ProducesResponseType(typeof(WorkflowInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkflowInstanceById(
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        var instance = await _workflowEngine.GetWorkflowInstanceByIdAsync(instanceId, cancellationToken);

        if (instance == null)
        {
            return NotFound(new { error = "Workflow instance not found" });
        }

        return Ok(instance);
    }

    // ==========================================================================
    // Stage Transitions
    // ==========================================================================

    /// <summary>
    /// Transition a workflow to a new stage.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/transition")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TransitionStage(
        Guid noticeId,
        [FromBody] TransitionStageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.TransitionStageAsync(
            noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Validate if a transition is allowed.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/validate-transition")]
    [ProducesResponseType(typeof(ValidationResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateTransition(
        Guid noticeId,
        [FromQuery] string targetStageKey,
        CancellationToken cancellationToken)
    {
        var (isValid, error) = await _workflowEngine.ValidateTransitionAsync(
            noticeId, targetStageKey, cancellationToken);

        return Ok(new ValidationResultDto(isValid, error));
    }

    /// <summary>
    /// Get available transitions for current stage.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/available-transitions")]
    [ProducesResponseType(typeof(List<WorkflowStageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableTransitions(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        var transitions = await _workflowEngine.GetAvailableTransitionsAsync(noticeId, cancellationToken);
        return Ok(transitions);
    }

    /// <summary>
    /// Bulk transition multiple notices.
    /// </summary>
    [HttpPost("bulk-transition")]
    [ProducesResponseType(typeof(BulkTransitionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkTransition(
        [FromBody] BulkTransitionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.BulkTransitionAsync(request, GetUserId(), cancellationToken);
        return Ok(result);
    }

    // ==========================================================================
    // Assignment
    // ==========================================================================

    /// <summary>
    /// Assign a workflow to a user or role.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/assign")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignWorkflow(
        Guid noticeId,
        [FromBody] AssignWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.AssignWorkflowAsync(
            noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Bulk assign workflows.
    /// </summary>
    [HttpPost("bulk-assign")]
    [ProducesResponseType(typeof(BulkTransitionResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkAssign(
        [FromBody] BulkAssignRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.BulkAssignAsync(request, GetUserId(), cancellationToken);
        return Ok(result);
    }

    // ==========================================================================
    // Workflow Control
    // ==========================================================================

    /// <summary>
    /// Pause a workflow.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/pause")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PauseWorkflow(
        Guid noticeId,
        [FromBody] PauseWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.PauseWorkflowAsync(
            noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Resume a paused workflow.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/resume")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResumeWorkflow(
        Guid noticeId,
        [FromBody] ResumeWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.ResumeWorkflowAsync(
            noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Cancel a workflow.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/cancel")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelWorkflow(
        Guid noticeId,
        [FromBody] CancelWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.CancelWorkflowAsync(
            noticeId, request.Reason, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    // ==========================================================================
    // History and Progress
    // ==========================================================================

    /// <summary>
    /// Get workflow history for a notice.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/history")]
    [ProducesResponseType(typeof(List<WorkflowHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkflowHistory(
        Guid noticeId,
        [FromQuery] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var history = await _workflowEngine.GetWorkflowHistoryAsync(noticeId, limit, cancellationToken);
        return Ok(history);
    }

    /// <summary>
    /// Get workflow progress for a notice.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/progress")]
    [ProducesResponseType(typeof(WorkflowProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkflowProgress(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        var progress = await _workflowEngine.GetWorkflowProgressAsync(noticeId, cancellationToken);

        if (progress == null)
        {
            return NotFound(new { error = "No workflow found for this notice" });
        }

        return Ok(progress);
    }

    // ==========================================================================
    // SLA Management
    // ==========================================================================

    /// <summary>
    /// Get SLA status for a workflow.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/sla")]
    [ProducesResponseType(typeof(SlaStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSlaStatus(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        var slaStatus = await _workflowEngine.GetSlaStatusAsync(noticeId, cancellationToken);

        if (slaStatus == null)
        {
            return NotFound(new { error = "No workflow found for this notice" });
        }

        return Ok(slaStatus);
    }

    // ==========================================================================
    // Parallel Execution
    // ==========================================================================

    /// <summary>
    /// Fork a workflow into parallel branches.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/fork")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForkWorkflow(
        Guid noticeId,
        [FromBody] ForkWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.ForkWorkflowAsync(
            noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        _logger.LogInformation(
            "User {UserId} forked workflow for notice {NoticeId} into {BranchCount} branches",
            GetUserId(), noticeId, request.TargetStageKeys.Count);

        return Ok(result);
    }

    /// <summary>
    /// Get active stage instances for a workflow (parallel execution).
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/stage-instances")]
    [ProducesResponseType(typeof(List<StageInstanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveStageInstances(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        var instances = await _workflowEngine.GetActiveStageInstancesAsync(noticeId, cancellationToken);
        return Ok(instances);
    }

    /// <summary>
    /// Complete a specific stage instance in a parallel workflow.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/stage-instances/{stageInstanceId:guid}/complete")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteStageInstance(
        Guid noticeId,
        Guid stageInstanceId,
        [FromBody] CompleteStageInstanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workflowEngine.CompleteStageInstanceAsync(
            noticeId, stageInstanceId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        _logger.LogInformation(
            "User {UserId} completed stage instance {StageInstanceId} for notice {NoticeId}",
            GetUserId(), stageInstanceId, noticeId);

        return Ok(result);
    }

    /// <summary>
    /// Get parallel branch status for a workflow.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/parallel-status")]
    [ProducesResponseType(typeof(ParallelBranchStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetParallelBranchStatus(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        var status = await _workflowEngine.GetParallelBranchStatusAsync(noticeId, cancellationToken);

        if (status == null)
        {
            return NotFound(new { error = "No parallel workflow found or workflow does not have parallel stages" });
        }

        return Ok(status);
    }

    // ==========================================================================
    // Templates
    // ==========================================================================

    /// <summary>
    /// Get available workflow templates.
    /// </summary>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(List<WorkflowTemplateSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableTemplates(CancellationToken cancellationToken)
    {
        var orgId = _orgService.OrganizationId
            ?? throw new InvalidOperationException("No organization context");

        var templates = await _workflowEngine.GetAvailableTemplatesAsync(orgId, cancellationToken);
        return Ok(templates);
    }

    /// <summary>
    /// Get a workflow template by ID.
    /// </summary>
    [HttpGet("templates/{templateId:guid}")]
    [ProducesResponseType(typeof(WorkflowTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplate(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var template = await _workflowEngine.GetTemplateAsync(templateId, cancellationToken);

        if (template == null)
        {
            return NotFound(new { error = "Template not found" });
        }

        return Ok(template);
    }

    /// <summary>
    /// Get the default workflow template.
    /// </summary>
    [HttpGet("templates/default")]
    [ProducesResponseType(typeof(WorkflowTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDefaultTemplate(CancellationToken cancellationToken)
    {
        var template = await _workflowEngine.GetDefaultTemplateAsync(cancellationToken);

        if (template == null)
        {
            return NotFound(new { error = "No default template configured" });
        }

        return Ok(template);
    }
}

// ==========================================================================
// Supporting DTOs
// ==========================================================================

public record ValidationResultDto(bool IsValid, string? Error);

public record CancelWorkflowRequest(string Reason);
