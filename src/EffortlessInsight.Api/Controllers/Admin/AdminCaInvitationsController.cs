using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing CA invitations.
/// </summary>
[Route("api/v1/admin/ca-invitations")]
[Authorize(Policy = "AdminAuthenticated")]
public class AdminCaInvitationsController : AdminControllerBase
{
    private readonly IAdminCaService _caService;
    private readonly IAdminAuditService _auditService;

    public AdminCaInvitationsController(
        IAdminCaService caService,
        IAdminAuditService auditService,
        ILogger<AdminCaInvitationsController> logger)
        : base(logger)
    {
        _caService = caService;
        _auditService = auditService;
    }

    /// <summary>
    /// Search and list CA invitations.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminCaInvitationListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvitations([FromQuery] AdminCaInvitationSearchParams searchParams)
    {
        if (!HasPermission(AdminPermissions.UsersView))
        {
            return Forbid();
        }

        var result = await _caService.ListInvitationsAsync(searchParams);
        return Success(result);
    }

    /// <summary>
    /// Cancel a pending CA invitation (admin override).
    /// </summary>
    [HttpPost("{invitationId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelInvitation(Guid invitationId, [FromBody] AdminCancelCaInvitationRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        try
        {
            await _caService.CancelInvitationAsync(invitationId, CurrentAdminId, request.Reason);
            return Success<object?>(null, "Invitation cancelled successfully");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundResponse(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message, "INVALID_OPERATION");
        }
    }
}
