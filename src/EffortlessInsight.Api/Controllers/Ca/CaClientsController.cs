using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Ca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers.Ca;

/// <summary>
/// CA client management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/ca/clients")]
[Authorize]
public class CaClientsController : ControllerBase
{
    private readonly ICaClientService _clientService;
    private readonly ICaProfileService _profileService;
    private readonly ICaDashboardService _dashboardService;
    private readonly ICaAuthorizationService _authService;
    private readonly ILogger<CaClientsController> _logger;

    public CaClientsController(
        ICaClientService clientService,
        ICaProfileService profileService,
        ICaDashboardService dashboardService,
        ICaAuthorizationService authService,
        ILogger<CaClientsController> logger)
    {
        _clientService = clientService;
        _profileService = profileService;
        _dashboardService = dashboardService;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Get all clients for the CA.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CaClientListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetClients(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();

        if (!await _profileService.IsCaAsync(userId, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_CA", "User is not a CA"));
        }

        var result = await _clientService.GetClientsAsync(userId, status, page, pageSize, ct);
        return Ok(new ApiResponse<CaClientListResponse>(true, result));
    }

    /// <summary>
    /// Get a specific client.
    /// </summary>
    [HttpGet("{relationshipId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CaClientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClient(
        Guid relationshipId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var client = await _clientService.GetClientAsync(userId, relationshipId, ct);

        if (client == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Client not found"));
        }

        return Ok(new ApiResponse<CaClientDto>(true, client));
    }

    /// <summary>
    /// Get dashboard for a specific client.
    /// </summary>
    [HttpGet("{relationshipId:guid}/dashboard")]
    [ProducesResponseType(typeof(ApiResponse<CaClientDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClientDashboard(
        Guid relationshipId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        try
        {
            var dashboard = await _dashboardService.GetClientDashboardAsync(userId, relationshipId, ct);
            return Ok(new ApiResponse<CaClientDashboardDto>(true, dashboard));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Update client reference/notes.
    /// </summary>
    [HttpPatch("{relationshipId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateClient(
        Guid relationshipId,
        [FromBody] UpdateCaClientRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var success = await _clientService.UpdateClientAsync(userId, relationshipId, request, ct);

        if (!success)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Client not found"));
        }

        return NoContent();
    }

    /// <summary>
    /// Revoke access to a client (CA can revoke their own access).
    /// </summary>
    [HttpPost("{relationshipId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeClient(
        Guid relationshipId,
        [FromBody] RevokeCaRelationshipRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var success = await _clientService.RevokeAsync(relationshipId, userId, request.Reason, ct);

        if (!success)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Client not found or already revoked"));
        }

        return NoContent();
    }

    /// <summary>
    /// Get GSTIN authorizations for a client.
    /// </summary>
    [HttpGet("{relationshipId:guid}/authorizations")]
    [ProducesResponseType(typeof(ApiResponse<List<CaGstinAuthorizationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuthorizations(
        Guid relationshipId,
        CancellationToken ct)
    {
        var authorizations = await _authService.GetAuthorizationsByRelationshipAsync(relationshipId, ct);
        return Ok(new ApiResponse<List<CaGstinAuthorizationDto>>(true, authorizations));
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }
}
