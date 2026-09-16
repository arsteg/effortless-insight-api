using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing CA profiles.
/// </summary>
[Route("api/v1/admin/cas")]
[Authorize(Policy = "AdminAuthenticated")]
public class AdminCasController : AdminControllerBase
{
    private readonly IAdminCaService _caService;
    private readonly IAdminAuditService _auditService;

    public AdminCasController(
        IAdminCaService caService,
        IAdminAuditService auditService,
        ILogger<AdminCasController> logger)
        : base(logger)
    {
        _caService = caService;
        _auditService = auditService;
    }

    /// <summary>
    /// Search and list CA profiles.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminCaListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCas([FromQuery] AdminCaSearchParams searchParams)
    {
        if (!HasPermission(AdminPermissions.UsersView))
        {
            return Forbid();
        }

        var result = await _caService.ListCasAsync(searchParams);
        return Success(result);
    }

    /// <summary>
    /// Get CA profile details.
    /// </summary>
    [HttpGet("{caProfileId:guid}")]
    [ProducesResponseType(typeof(AdminCaProfileDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCa(Guid caProfileId)
    {
        if (!HasPermission(AdminPermissions.UsersView))
        {
            return Forbid();
        }

        var result = await _caService.GetCaAsync(caProfileId);
        if (result == null)
        {
            return NotFoundResponse("CA profile not found");
        }

        return Success(result);
    }

    /// <summary>
    /// Verify a CA profile.
    /// </summary>
    [HttpPost("{caProfileId:guid}/verify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyCa(Guid caProfileId, [FromBody] AdminVerifyCaRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        try
        {
            await _caService.VerifyCaAsync(caProfileId, CurrentAdminId, request);
            return Success<object?>(null, "CA verified successfully");
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

    /// <summary>
    /// Revoke CA verification.
    /// </summary>
    [HttpPost("{caProfileId:guid}/revoke-verification")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeCaVerification(Guid caProfileId, [FromBody] AdminRevokeCaVerificationRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        try
        {
            await _caService.RevokeCaVerificationAsync(caProfileId, CurrentAdminId, request.Reason);
            return Success<object?>(null, "CA verification revoked successfully");
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

    /// <summary>
    /// Suspend a CA profile.
    /// </summary>
    [HttpPost("{caProfileId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SuspendCa(Guid caProfileId, [FromBody] AdminSuspendCaRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        try
        {
            await _caService.SuspendCaAsync(caProfileId, CurrentAdminId, request);
            return Success<object?>(null, "CA suspended successfully");
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

    /// <summary>
    /// Unsuspend a CA profile.
    /// </summary>
    [HttpPost("{caProfileId:guid}/unsuspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UnsuspendCa(Guid caProfileId)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        try
        {
            await _caService.UnsuspendCaAsync(caProfileId, CurrentAdminId);
            return Success<object?>(null, "CA unsuspended successfully");
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
