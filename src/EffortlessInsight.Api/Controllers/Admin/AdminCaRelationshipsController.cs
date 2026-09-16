using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing CA-Client relationships.
/// </summary>
[Route("api/v1/admin/ca-relationships")]
[Authorize(Policy = "AdminAuthenticated")]
public class AdminCaRelationshipsController : AdminControllerBase
{
    private readonly IAdminCaService _caService;
    private readonly IAdminAuditService _auditService;

    public AdminCaRelationshipsController(
        IAdminCaService caService,
        IAdminAuditService auditService,
        ILogger<AdminCaRelationshipsController> logger)
        : base(logger)
    {
        _caService = caService;
        _auditService = auditService;
    }

    /// <summary>
    /// Search and list CA-Client relationships.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminCaRelationshipListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRelationships([FromQuery] AdminCaRelationshipSearchParams searchParams)
    {
        if (!HasPermission(AdminPermissions.UsersView))
        {
            return Forbid();
        }

        var result = await _caService.ListRelationshipsAsync(searchParams);
        return Success(result);
    }

    /// <summary>
    /// Get CA-Client relationship details.
    /// </summary>
    [HttpGet("{relationshipId:guid}")]
    [ProducesResponseType(typeof(AdminCaRelationshipDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRelationship(Guid relationshipId)
    {
        if (!HasPermission(AdminPermissions.UsersView))
        {
            return Forbid();
        }

        var result = await _caService.GetRelationshipAsync(relationshipId);
        if (result == null)
        {
            return NotFoundResponse("Relationship not found");
        }

        return Success(result);
    }

    /// <summary>
    /// Revoke a CA-Client relationship (admin override).
    /// </summary>
    [HttpPost("{relationshipId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeRelationship(Guid relationshipId, [FromBody] AdminRevokeCaRelationshipRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        try
        {
            await _caService.RevokeRelationshipAsync(relationshipId, CurrentAdminId, request.Reason);
            return Success<object?>(null, "Relationship revoked successfully");
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
