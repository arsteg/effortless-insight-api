using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Features.Workflows.Dtos;
using EffortlessInsight.Api.Features.Workflows.Services;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EffortlessInsight.Api.Features.Workflows;

[Authorize]
[ApiController]
[Route("api/v1/workflows")]
public class WorkflowController : ControllerBase
{
    private readonly IWorkflowEngineService _workflowService;
    private readonly ICurrentOrganizationService _currentOrgService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WorkflowController> _logger;

    public WorkflowController(
        IWorkflowEngineService workflowService,
        ICurrentOrganizationService currentOrgService,
        ApplicationDbContext context,
        ILogger<WorkflowController> logger)
    {
        _workflowService = workflowService;
        _currentOrgService = currentOrgService;
        _context = context;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in claims");
        }
        return userId;
    }

    private Guid GetOrganizationId()
    {
        var orgId = _currentOrgService.OrganizationId;
        if (!orgId.HasValue)
        {
            throw new UnauthorizedAccessException("Organization context required");
        }
        return orgId.Value;
    }

    /// <summary>
    /// Verifies that the notice belongs to the current organization.
    /// </summary>
    private async Task<bool> VerifyNoticeAccessAsync(Guid noticeId, CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        return await _context.Notices
            .AnyAsync(n => n.Id == noticeId && n.OrganizationId == orgId && n.DeletedAt == null, cancellationToken);
    }

    /// <summary>
    /// Verifies that multiple notices belong to the current organization.
    /// </summary>
    private async Task<List<Guid>> GetAuthorizedNoticeIdsAsync(IEnumerable<Guid> noticeIds, CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        return await _context.Notices
            .Where(n => noticeIds.Contains(n.Id) && n.OrganizationId == orgId && n.DeletedAt == null)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    #region Workflow Instance Endpoints

    /// <summary>
    /// Starts a workflow for a notice.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransitionResult>> StartWorkflow(
        [FromBody] StartWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(request.NoticeId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _workflowService.StartWorkflowAsync(request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Gets the active workflow instance for a notice.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}")]
    [ProducesResponseType(typeof(WorkflowInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WorkflowInstanceDto>> GetWorkflowInstance(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var instance = await _workflowService.GetWorkflowInstanceAsync(noticeId, cancellationToken);

        if (instance == null)
        {
            return NotFound(new { message = "No active workflow found for this notice" });
        }

        return Ok(instance);
    }

    /// <summary>
    /// Gets workflow instances for multiple notices (batch operation).
    /// </summary>
    [HttpPost("notices/batch")]
    [ProducesResponseType(typeof(Dictionary<Guid, WorkflowInstanceSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Dictionary<Guid, WorkflowInstanceSummaryDto>>> GetWorkflowInstancesBatch(
        [FromBody] List<Guid> noticeIds,
        CancellationToken cancellationToken)
    {
        if (noticeIds.Count > 100)
        {
            return BadRequest(new { message = "Maximum 100 notices per batch request" });
        }

        // Filter to only authorized notices
        var authorizedIds = await GetAuthorizedNoticeIdsAsync(noticeIds, cancellationToken);

        var instances = await _workflowService.GetWorkflowInstancesForNoticesAsync(authorizedIds, cancellationToken);
        return Ok(instances);
    }

    #endregion

    #region Stage Transition Endpoints

    /// <summary>
    /// Transitions a workflow to a new stage.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/transition")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransitionResult>> TransitionStage(
        Guid noticeId,
        [FromBody] TransitionStageRequest request,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _workflowService.TransitionStageAsync(noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Validates if a transition is allowed.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/validate-transition/{targetStageKey}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> ValidateTransition(
        Guid noticeId,
        string targetStageKey,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var (isValid, error) = await _workflowService.ValidateTransitionAsync(noticeId, targetStageKey, cancellationToken);
        return Ok(new { isValid, error });
    }

    /// <summary>
    /// Gets available transitions for the current stage.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/available-transitions")]
    [ProducesResponseType(typeof(List<WorkflowStageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<WorkflowStageDto>>> GetAvailableTransitions(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var transitions = await _workflowService.GetAvailableTransitionsAsync(noticeId, cancellationToken);
        return Ok(transitions);
    }

    /// <summary>
    /// Performs bulk stage transition for multiple notices.
    /// </summary>
    [HttpPost("bulk/transition")]
    [ProducesResponseType(typeof(BulkTransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkTransitionResult>> BulkTransition(
        [FromBody] BulkTransitionRequest request,
        CancellationToken cancellationToken)
    {
        // Filter to only authorized notices
        var authorizedIds = await GetAuthorizedNoticeIdsAsync(request.NoticeIds, cancellationToken);

        if (authorizedIds.Count == 0)
        {
            return BadRequest(new { message = "No authorized notices found" });
        }

        // Create a new request with only authorized notices
        var authorizedRequest = request with { NoticeIds = authorizedIds };
        var result = await _workflowService.BulkTransitionAsync(authorizedRequest, GetUserId(), cancellationToken);
        return Ok(result);
    }

    #endregion

    #region Assignment Endpoints

    /// <summary>
    /// Assigns a workflow to a user or role.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/assign")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransitionResult>> AssignWorkflow(
        Guid noticeId,
        [FromBody] AssignWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _workflowService.AssignWorkflowAsync(noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Bulk assigns workflows to a user or role.
    /// </summary>
    [HttpPost("bulk/assign")]
    [ProducesResponseType(typeof(BulkTransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkTransitionResult>> BulkAssign(
        [FromBody] BulkAssignRequest request,
        CancellationToken cancellationToken)
    {
        // Filter to only authorized notices
        var authorizedIds = await GetAuthorizedNoticeIdsAsync(request.NoticeIds, cancellationToken);

        if (authorizedIds.Count == 0)
        {
            return BadRequest(new { message = "No authorized notices found" });
        }

        // Create a new request with only authorized notices
        var authorizedRequest = request with { NoticeIds = authorizedIds };
        var result = await _workflowService.BulkAssignAsync(authorizedRequest, GetUserId(), cancellationToken);
        return Ok(result);
    }

    #endregion

    #region Workflow Control Endpoints

    /// <summary>
    /// Pauses a workflow instance.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/pause")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransitionResult>> PauseWorkflow(
        Guid noticeId,
        [FromBody] PauseWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _workflowService.PauseWorkflowAsync(noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Resumes a paused workflow instance.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/resume")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransitionResult>> ResumeWorkflow(
        Guid noticeId,
        [FromBody] ResumeWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _workflowService.ResumeWorkflowAsync(noticeId, request, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Cancels a workflow instance.
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/cancel")]
    [ProducesResponseType(typeof(TransitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TransitionResult>> CancelWorkflow(
        Guid noticeId,
        [FromBody] CancelWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var result = await _workflowService.CancelWorkflowAsync(noticeId, request.Reason, GetUserId(), cancellationToken);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    #endregion

    #region History and Progress Endpoints

    /// <summary>
    /// Gets workflow history for a notice.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/history")]
    [ProducesResponseType(typeof(List<WorkflowHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<WorkflowHistoryDto>>> GetWorkflowHistory(
        Guid noticeId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        // Enforce maximum limit to prevent memory issues
        var effectiveLimit = Math.Min(limit ?? 100, 500);
        var history = await _workflowService.GetWorkflowHistoryAsync(noticeId, effectiveLimit, cancellationToken);
        return Ok(history);
    }

    /// <summary>
    /// Gets workflow progress information.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/progress")]
    [ProducesResponseType(typeof(WorkflowProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WorkflowProgressDto>> GetWorkflowProgress(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var progress = await _workflowService.GetWorkflowProgressAsync(noticeId, cancellationToken);

        if (progress == null)
        {
            return NotFound(new { message = "No active workflow found for this notice" });
        }

        return Ok(progress);
    }

    #endregion

    #region SLA Endpoints

    /// <summary>
    /// Gets the SLA status for a workflow instance.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/sla")]
    [ProducesResponseType(typeof(SlaStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SlaStatusDto>> GetSlaStatus(
        Guid noticeId,
        CancellationToken cancellationToken)
    {
        // Verify organization access
        if (!await VerifyNoticeAccessAsync(noticeId, cancellationToken))
        {
            return Forbid();
        }

        var slaStatus = await _workflowService.GetSlaStatusAsync(noticeId, cancellationToken);

        if (slaStatus == null)
        {
            return NotFound(new { message = "No active workflow found for this notice" });
        }

        return Ok(slaStatus);
    }

    #endregion

    #region Template Endpoints

    /// <summary>
    /// Gets available workflow templates for the current organization.
    /// </summary>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(List<WorkflowTemplateSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WorkflowTemplateSummaryDto>>> GetAvailableTemplates(
        CancellationToken cancellationToken)
    {
        var orgId = _currentOrgService.OrganizationId
            ?? throw new UnauthorizedAccessException("Organization context required");

        var templates = await _workflowService.GetAvailableTemplatesAsync(orgId, cancellationToken);
        return Ok(templates);
    }

    /// <summary>
    /// Gets a workflow template by ID.
    /// </summary>
    [HttpGet("templates/{templateId:guid}")]
    [ProducesResponseType(typeof(WorkflowTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowTemplateDto>> GetTemplate(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var template = await _workflowService.GetTemplateAsync(templateId, cancellationToken);

        if (template == null)
        {
            return NotFound(new { message = "Template not found" });
        }

        return Ok(template);
    }

    /// <summary>
    /// Gets the default workflow template.
    /// </summary>
    [HttpGet("templates/default")]
    [ProducesResponseType(typeof(WorkflowTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowTemplateDto>> GetDefaultTemplate(
        CancellationToken cancellationToken)
    {
        var template = await _workflowService.GetDefaultTemplateAsync(cancellationToken);

        if (template == null)
        {
            return NotFound(new { message = "Default template not found" });
        }

        return Ok(template);
    }

    #endregion
}

/// <summary>
/// Request model for cancelling a workflow.
/// </summary>
public record CancelWorkflowRequest
{
    public string Reason { get; init; } = string.Empty;
}
