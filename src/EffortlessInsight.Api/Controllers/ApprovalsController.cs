using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1")]
public class ApprovalsController : ControllerBase
{
    private readonly IApprovalService _approvalService;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<ApprovalsController> _logger;

    public ApprovalsController(
        IApprovalService approvalService,
        ICurrentOrganizationService orgService,
        ILogger<ApprovalsController> logger)
    {
        _approvalService = approvalService;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);

    private Guid GetOrganizationId() =>
        _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");

    private string? GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    // ==========================================================================
    // Approval Chain Management
    // ==========================================================================

    /// <summary>
    /// Get all approval chains for the organization
    /// </summary>
    [HttpGet("approval-chains")]
    [ProducesResponseType(typeof(List<ApprovalChainDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApprovalChains([FromQuery] bool? isActive = null)
    {
        var orgId = GetOrganizationId();
        var chains = await _approvalService.GetChainsAsync(orgId, isActive);

        var result = chains.Select(c => new ApprovalChainDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            TriggerEvent = c.TriggerEvent,
            IsActive = c.IsActive,
            IsParallel = c.IsParallel,
            MinApprovalsRequired = c.MinApprovalsRequired,
            DefaultTimeoutHours = c.DefaultTimeoutHours,
            Steps = c.Steps.OrderBy(s => s.StepOrder).Select(s => new ApprovalStepDto
            {
                Id = s.Id,
                StepOrder = s.StepOrder,
                Name = s.Name,
                ApproverType = s.ApproverType,
                ApproverId = s.ApproverId,
                ApproverName = s.Approver?.Name,
                ApproverRole = s.ApproverRole,
                IsOptional = s.IsOptional,
                TimeoutHours = s.TimeoutHours,
                AllowDelegation = s.AllowDelegation,
                Instructions = s.Instructions
            }).ToList(),
            CreatedAt = c.CreatedAt
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Get an approval chain by ID
    /// </summary>
    [HttpGet("approval-chains/{chainId:guid}")]
    [ProducesResponseType(typeof(ApprovalChainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApprovalChain(Guid chainId)
    {
        var orgId = GetOrganizationId();
        var chain = await _approvalService.GetChainByIdAsync(chainId, orgId);

        if (chain == null)
            return NotFound(new { error = "Approval chain not found" });

        var result = new ApprovalChainDto
        {
            Id = chain.Id,
            Name = chain.Name,
            Description = chain.Description,
            TriggerEvent = chain.TriggerEvent,
            IsActive = chain.IsActive,
            IsParallel = chain.IsParallel,
            MinApprovalsRequired = chain.MinApprovalsRequired,
            DefaultTimeoutHours = chain.DefaultTimeoutHours,
            Steps = chain.Steps.OrderBy(s => s.StepOrder).Select(s => new ApprovalStepDto
            {
                Id = s.Id,
                StepOrder = s.StepOrder,
                Name = s.Name,
                ApproverType = s.ApproverType,
                ApproverId = s.ApproverId,
                ApproverName = s.Approver?.Name,
                ApproverRole = s.ApproverRole,
                IsOptional = s.IsOptional,
                TimeoutHours = s.TimeoutHours,
                AllowDelegation = s.AllowDelegation,
                Instructions = s.Instructions
            }).ToList(),
            CreatedAt = chain.CreatedAt
        };

        return Ok(result);
    }

    /// <summary>
    /// Create a new approval chain
    /// </summary>
    [HttpPost("approval-chains")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(typeof(ApprovalChainDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateApprovalChain([FromBody] CreateApprovalChainRequest request)
    {
        try
        {
            var orgId = GetOrganizationId();
            var chain = await _approvalService.CreateChainAsync(request, orgId);

            var result = new ApprovalChainDto
            {
                Id = chain.Id,
                Name = chain.Name,
                Description = chain.Description,
                TriggerEvent = chain.TriggerEvent,
                IsActive = chain.IsActive,
                IsParallel = chain.IsParallel,
                MinApprovalsRequired = chain.MinApprovalsRequired,
                DefaultTimeoutHours = chain.DefaultTimeoutHours,
                Steps = chain.Steps.OrderBy(s => s.StepOrder).Select(s => new ApprovalStepDto
                {
                    Id = s.Id,
                    StepOrder = s.StepOrder,
                    Name = s.Name,
                    ApproverType = s.ApproverType,
                    ApproverId = s.ApproverId,
                    ApproverName = s.Approver?.Name,
                    ApproverRole = s.ApproverRole,
                    IsOptional = s.IsOptional,
                    TimeoutHours = s.TimeoutHours,
                    AllowDelegation = s.AllowDelegation,
                    Instructions = s.Instructions
                }).ToList(),
                CreatedAt = chain.CreatedAt
            };

            return CreatedAtAction(nameof(GetApprovalChain), new { chainId = chain.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update an approval chain
    /// </summary>
    [HttpPatch("approval-chains/{chainId:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(typeof(ApprovalChainDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateApprovalChain(Guid chainId, [FromBody] UpdateApprovalChainRequest request)
    {
        try
        {
            var orgId = GetOrganizationId();
            var chain = await _approvalService.UpdateChainAsync(chainId, request, orgId);

            var result = new ApprovalChainDto
            {
                Id = chain.Id,
                Name = chain.Name,
                Description = chain.Description,
                TriggerEvent = chain.TriggerEvent,
                IsActive = chain.IsActive,
                IsParallel = chain.IsParallel,
                MinApprovalsRequired = chain.MinApprovalsRequired,
                DefaultTimeoutHours = chain.DefaultTimeoutHours,
                Steps = chain.Steps.OrderBy(s => s.StepOrder).Select(s => new ApprovalStepDto
                {
                    Id = s.Id,
                    StepOrder = s.StepOrder,
                    Name = s.Name,
                    ApproverType = s.ApproverType,
                    ApproverId = s.ApproverId,
                    ApproverName = s.Approver?.Name,
                    ApproverRole = s.ApproverRole,
                    IsOptional = s.IsOptional,
                    TimeoutHours = s.TimeoutHours,
                    AllowDelegation = s.AllowDelegation,
                    Instructions = s.Instructions
                }).ToList(),
                CreatedAt = chain.CreatedAt
            };

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete an approval chain
    /// </summary>
    [HttpDelete("approval-chains/{chainId:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteApprovalChain(Guid chainId)
    {
        try
        {
            var orgId = GetOrganizationId();
            await _approvalService.DeleteChainAsync(chainId, orgId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Approval Request Endpoints
    // ==========================================================================

    /// <summary>
    /// Submit a notice for approval
    /// </summary>
    [HttpPost("approval-requests")]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitForApproval([FromBody] SubmitApprovalRequest request)
    {
        try
        {
            var orgId = GetOrganizationId();
            var userId = GetUserId();
            var approvalRequest = await _approvalService.SubmitForApprovalAsync(request, orgId, userId);

            var result = MapToApprovalRequestDto(approvalRequest);
            return CreatedAtAction(nameof(GetApprovalRequest), new { requestId = approvalRequest.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get an approval request by ID
    /// </summary>
    [HttpGet("approval-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApprovalRequest(Guid requestId)
    {
        var orgId = GetOrganizationId();
        var request = await _approvalService.GetRequestByIdAsync(requestId, orgId);

        if (request == null)
            return NotFound(new { error = "Approval request not found" });

        return Ok(MapToApprovalRequestDto(request));
    }

    /// <summary>
    /// Get pending approvals for the current user
    /// </summary>
    [HttpGet("approval-requests/pending")]
    [ProducesResponseType(typeof(List<ApprovalRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingApprovals()
    {
        var orgId = GetOrganizationId();
        var userId = GetUserId();
        var requests = await _approvalService.GetPendingApprovalsAsync(orgId, userId);

        return Ok(requests.Select(MapToApprovalRequestDto).ToList());
    }

    /// <summary>
    /// Get approval requests for a specific notice
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/approval-requests")]
    [ProducesResponseType(typeof(List<ApprovalRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApprovalRequestsForNotice(Guid noticeId)
    {
        var orgId = GetOrganizationId();
        var requests = await _approvalService.GetRequestsByNoticeAsync(noticeId, orgId);

        return Ok(requests.Select(MapToApprovalRequestDto).ToList());
    }

    // ==========================================================================
    // Approval Actions
    // ==========================================================================

    /// <summary>
    /// Approve an approval request
    /// </summary>
    [HttpPost("approval-requests/{requestId:guid}/approve")]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid requestId, [FromBody] ApprovalActionRequest request)
    {
        try
        {
            var userId = GetUserId();
            var ipAddress = GetClientIp();
            var approvalRequest = await _approvalService.ApproveAsync(requestId, request.Comments, userId, ipAddress);

            return Ok(MapToApprovalRequestDto(approvalRequest));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reject an approval request
    /// </summary>
    [HttpPost("approval-requests/{requestId:guid}/reject")]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid requestId, [FromBody] ApprovalActionRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Comments))
                return BadRequest(new { error = "Comments are required when rejecting" });

            var userId = GetUserId();
            var ipAddress = GetClientIp();
            var approvalRequest = await _approvalService.RejectAsync(requestId, request.Comments, userId, ipAddress);

            return Ok(MapToApprovalRequestDto(approvalRequest));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delegate an approval request to another user
    /// </summary>
    [HttpPost("approval-requests/{requestId:guid}/delegate")]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delegate(Guid requestId, [FromBody] DelegateApprovalRequest request)
    {
        try
        {
            var userId = GetUserId();
            var ipAddress = GetClientIp();
            var approvalRequest = await _approvalService.DelegateAsync(
                requestId, request.DelegateToUserId, request.Reason, userId, ipAddress);

            return Ok(MapToApprovalRequestDto(approvalRequest));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Recall an approval request (cancel it)
    /// </summary>
    [HttpPost("approval-requests/{requestId:guid}/recall")]
    [ProducesResponseType(typeof(ApprovalRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Recall(Guid requestId)
    {
        try
        {
            var userId = GetUserId();
            var approvalRequest = await _approvalService.RecallAsync(requestId, userId);

            return Ok(MapToApprovalRequestDto(approvalRequest));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Helper Methods
    // ==========================================================================

    private static ApprovalRequestDto MapToApprovalRequestDto(Data.Entities.ApprovalRequest request)
    {
        return new ApprovalRequestDto
        {
            Id = request.Id,
            ApprovalChainId = request.ApprovalChainId,
            ApprovalChainName = request.ApprovalChain?.Name ?? string.Empty,
            NoticeId = request.NoticeId,
            NoticeNumber = request.Notice?.NoticeNumber,
            ResponseId = request.ResponseId,
            RequestedById = request.RequestedById,
            RequestedByName = request.RequestedBy?.Name ?? string.Empty,
            CurrentStep = request.CurrentStep,
            TotalSteps = request.ApprovalChain?.Steps?.Count ?? 0,
            Status = request.Status,
            CurrentStepDeadline = request.CurrentStepDeadline,
            CompletedAt = request.CompletedAt,
            RequestNotes = request.RequestNotes,
            Actions = request.Actions?.Select(a => new ApprovalActionDto
            {
                Id = a.Id,
                StepOrder = a.ApprovalStep?.StepOrder ?? 0,
                StepName = a.ApprovalStep?.Name ?? string.Empty,
                ActorId = a.ActorId,
                ActorName = a.Actor?.Name ?? string.Empty,
                ActionType = a.ActionType,
                Comments = a.Comments,
                DelegatedToId = a.DelegatedToId,
                DelegatedToName = a.DelegatedTo?.Name,
                IsAutomatic = a.IsAutomatic,
                CreatedAt = a.CreatedAt
            }).ToList() ?? [],
            CreatedAt = request.CreatedAt
        };
    }
}

// Additional DTOs for controller actions
public record ApprovalActionRequest
{
    public string? Comments { get; init; }
}
