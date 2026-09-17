using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Auth;
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
    private readonly IJwtService _jwtService;
    private readonly ILogger<CaContextController> _logger;

    public CaContextController(
        ICaContextService contextService,
        ICaProfileService profileService,
        IJwtService jwtService,
        ILogger<CaContextController> logger)
    {
        _contextService = contextService;
        _profileService = profileService;
        _jwtService = jwtService;
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

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _contextService.SelectClientAsync(
            userId, request.ClientRelationshipId, ipAddress, userAgent, ct);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(false, result.ErrorCode!, result.ErrorMessage!));
        }

        var response = new SelectClientContextResponse(
            AccessToken: result.AccessToken!,
            RefreshToken: result.RefreshToken!,
            TokenType: "Bearer",
            ExpiresIn: _jwtService.GetAccessTokenExpiryMinutes() * 60,
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

        // The selected client is whatever the token was issued for. Everything else about
        // that engagement is read fresh from the database, so a revoked relationship stops
        // being reported as selected without waiting for the token to roll over.
        Guid? selectedRelationshipId = null;
        if (Guid.TryParse(User.FindFirst(CaClaimTypes.ClientRelationshipId)?.Value, out var relId))
        {
            selectedRelationshipId = relId;
        }

        var context = await _contextService.GetCurrentContextAsync(userId, selectedRelationshipId, ct);

        if (context == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_CA", "User is not a CA"));
        }

        return Ok(new ApiResponse<CaContextDto>(true, context));
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }
}
