using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Ca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers.Ca;

/// <summary>
/// CA profile management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/ca")]
[Authorize]
public class CaController : ControllerBase
{
    private readonly ICaProfileService _profileService;
    private readonly ICaDashboardService _dashboardService;
    private readonly ILogger<CaController> _logger;

    public CaController(
        ICaProfileService profileService,
        ICaDashboardService dashboardService,
        ILogger<CaController> logger)
    {
        _profileService = profileService;
        _dashboardService = dashboardService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new CA user.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CaRegisterResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] CaRegisterRequest request,
        CancellationToken ct)
    {
        var result = await _profileService.RegisterCaAsync(request, ct);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(false, result.ErrorCode!, result.ErrorMessage!));
        }

        var response = new CaRegisterResponse(
            UserId: result.UserId!.Value,
            CaProfileId: result.CaProfileId!.Value,
            Email: request.Email,
            Name: request.Name,
            EmailVerified: false,
            Message: "Registration successful. Please verify your email."
        );

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<CaRegisterResponse>(true, response));
    }

    /// <summary>
    /// Get current CA's profile.
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(ApiResponse<CaProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var profile = await _profileService.GetProfileDtoAsync(userId, ct);

        if (profile == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_CA", "User is not registered as a CA"));
        }

        return Ok(new ApiResponse<CaProfileDto>(true, profile));
    }

    /// <summary>
    /// Update CA profile.
    /// </summary>
    [HttpPatch("profile")]
    [ProducesResponseType(typeof(ApiResponse<CaProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateCaProfileRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        try
        {
            await _profileService.UpdateAsync(userId, request, ct);
            var profile = await _profileService.GetProfileDtoAsync(userId, ct);
            return Ok(new ApiResponse<CaProfileDto>(true, profile!));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Get CA dashboard overview.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<CaDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        if (!await _profileService.IsCaAsync(userId, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_CA", "User is not a CA"));
        }

        var dashboard = await _dashboardService.GetDashboardAsync(userId, ct);
        return Ok(new ApiResponse<CaDashboardDto>(true, dashboard));
    }

    /// <summary>
    /// Create an organization for CA onboarding.
    /// Unlike BO, GSTIN is optional for CA organizations.
    /// </summary>
    [HttpPost("organization")]
    [ProducesResponseType(typeof(ApiResponse<CreateCaOrganizationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateOrganization(
        [FromBody] CreateCaOrganizationRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        // Verify user is a CA
        if (!await _profileService.IsCaAsync(userId, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_CA", "User is not a CA"));
        }

        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _profileService.CreateOrganizationAsync(
                userId, request, ipAddress, userAgent, ct);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<CreateCaOrganizationResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ALREADY_HAS_ORGANIZATION"))
        {
            return BadRequest(new ApiErrorResponse(false, "ALREADY_HAS_ORGANIZATION",
                "User already has an organization"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ORG_NAME_EXISTS"))
        {
            return BadRequest(new ApiErrorResponse(false, "ORG_NAME_EXISTS",
                "An organization with this name already exists"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_GSTIN"))
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_GSTIN",
                ex.Message.Replace("INVALID_GSTIN: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("GSTIN_EXISTS"))
        {
            return BadRequest(new ApiErrorResponse(false, "GSTIN_EXISTS",
                "This GSTIN is already registered with another organization"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", "User not found"));
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }
}
