using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Ca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers.Ca;

/// <summary>
/// CA client context management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/ca/context")]
[Authorize]
public class CaContextController : ControllerBase
{
    private readonly ICaContextService _contextService;
    private readonly ICaProfileService _profileService;
    private readonly ILogger<CaContextController> _logger;

    public CaContextController(
        ICaContextService contextService,
        ICaProfileService profileService,
        ILogger<CaContextController> logger)
    {
        _contextService = contextService;
        _profileService = profileService;
        _logger = logger;
    }

    /// <summary>
    /// Select a client context for the CA.
    /// Returns new JWT tokens with CA context claims.
    /// </summary>
    [HttpPost("select")]
    [ProducesResponseType(typeof(ApiResponse<SelectClientContextResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SelectClient(
        [FromBody] SelectClientContextRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        if (!await _profileService.IsCaAsync(userId, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_CA", "User is not a CA"));
        }

        var result = await _contextService.SelectClientAsync(userId, request.ClientRelationshipId, ct);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(false, result.ErrorCode!, result.ErrorMessage!));
        }

        var response = new SelectClientContextResponse(
            AccessToken: result.AccessToken!,
            RefreshToken: result.RefreshToken!,
            TokenType: "Bearer",
            ExpiresIn: 3600, // 1 hour
            Context: result.Context!
        );

        return Ok(new ApiResponse<SelectClientContextResponse>(true, response));
    }

    /// <summary>
    /// Get current CA context (without client selected).
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResponse<CaContextDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentContext(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var context = await _contextService.GetCurrentContextAsync(userId, ct);

        if (context == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_CA", "User is not a CA"));
        }

        // Check if there's a client context in the current token
        var clientRelIdClaim = User.FindFirst("ca_client_rel_id")?.Value;
        if (!string.IsNullOrEmpty(clientRelIdClaim))
        {
            var orgIdClaim = User.FindFirst("ca_client_org_id")?.Value;
            var gstinsClaim = User.FindFirst("ca_authorized_gstins")?.Value;
            var permsClaim = User.FindFirst("ca_permissions")?.Value;

            context = context with
            {
                SelectedClientRelationshipId = Guid.Parse(clientRelIdClaim),
                SelectedOrganizationId = !string.IsNullOrEmpty(orgIdClaim) ? Guid.Parse(orgIdClaim) : null,
                AuthorizedGstins = !string.IsNullOrEmpty(gstinsClaim) ? gstinsClaim.Split(',').ToList() : [],
                Permissions = !string.IsNullOrEmpty(permsClaim) ? permsClaim.Split(',').ToList() : [],
                ContextSetAt = DateTime.UtcNow
            };
        }

        return Ok(new ApiResponse<CaContextDto>(true, context));
    }

    /// <summary>
    /// Clear client context (go back to client list).
    /// Client should discard current token and use a non-CA-context token.
    /// </summary>
    [HttpPost("clear")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearContext(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await _contextService.ClearContextAsync(userId, ct);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }
}
