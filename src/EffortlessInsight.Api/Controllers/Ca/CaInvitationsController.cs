using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Ca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers.Ca;

/// <summary>
/// CA invitation management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/ca/invitations")]
[Authorize]
public class CaInvitationsController : ControllerBase
{
    private readonly ICaInvitationService _invitationService;
    private readonly ICaProfileService _profileService;
    private readonly ILogger<CaInvitationsController> _logger;

    public CaInvitationsController(
        ICaInvitationService invitationService,
        ICaProfileService profileService,
        ILogger<CaInvitationsController> logger)
    {
        _invitationService = invitationService;
        _profileService = profileService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new invitation to a Business Owner (CA invites BO).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateCaInvitationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateInvitation(
        [FromBody] CreateCaInvitationRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        if (!await _profileService.IsCaAsync(userId, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_CA", "User is not a CA"));
        }

        var result = await _invitationService.CreateInvitationAsync(userId, request, ct);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(false, result.ErrorCode!, result.ErrorMessage!));
        }

        var response = new CreateCaInvitationResponse(
            InvitationId: result.InvitationId!.Value,
            InviteeEmail: request.Email,
            Gstin: request.Gstin,
            Status: "pending",
            ExpiresAt: DateTime.UtcNow.AddDays(7),
            Message: "Invitation sent successfully"
        );

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<CreateCaInvitationResponse>(true, response));
    }

    /// <summary>
    /// Get invitations sent by the CA.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CaInvitationListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvitations(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await _invitationService.GetSentByCaAsync(userId, status, page, pageSize, ct);
        return Ok(new ApiResponse<CaInvitationListResponse>(true, result));
    }

    /// <summary>
    /// Cancel a pending invitation.
    /// </summary>
    [HttpPost("{invitationId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelInvitation(
        Guid invitationId,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var success = await _invitationService.CancelAsync(invitationId, userId, ct);

        if (!success)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Invitation not found or cannot be cancelled"));
        }

        return NoContent();
    }

    /// <summary>
    /// Resend an invitation.
    /// </summary>
    [HttpPost("{invitationId:guid}/resend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendInvitation(
        Guid invitationId,
        [FromBody] ResendCaInvitationRequest? request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var success = await _invitationService.ResendAsync(invitationId, userId, request?.UpdatedMessage, ct);

        if (!success)
        {
            return BadRequest(new ApiErrorResponse(false, "RESEND_FAILED", "Cannot resend invitation (not found or limit reached)"));
        }

        return NoContent();
    }

    /// <summary>
    /// Validate an invitation token (for displaying invitation details before accepting).
    /// </summary>
    [HttpGet("validate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CaInvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateToken(
        [FromQuery] string token,
        CancellationToken ct)
    {
        var invitation = await _invitationService.ValidateTokenAsync(token, ct);

        if (invitation == null)
        {
            return NotFound(new ApiErrorResponse(false, "INVALID_TOKEN", "Invalid or expired invitation token"));
        }

        return Ok(new ApiResponse<CaInvitationDto>(true, invitation));
    }

    /// <summary>
    /// Accept an invitation.
    /// </summary>
    [HttpPost("accept")]
    [ProducesResponseType(typeof(ApiResponse<AcceptCaInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AcceptInvitation(
        [FromBody] AcceptCaInvitationRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _invitationService.AcceptAsync(request.Token, userId, ct);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(false, result.ErrorCode!, result.ErrorMessage!));
        }

        var response = new AcceptCaInvitationResponse(
            RelationshipId: result.RelationshipId!.Value,
            Gstin: result.Gstin!,
            Message: "Invitation accepted successfully",
            RequiresOrganizationSetup: result.RequiresOrganizationSetup
        );

        return Ok(new ApiResponse<AcceptCaInvitationResponse>(true, response));
    }

    /// <summary>
    /// Decline an invitation.
    /// </summary>
    [HttpPost("decline")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineInvitation(
        [FromBody] DeclineCaInvitationRequest request,
        CancellationToken ct)
    {
        var success = await _invitationService.DeclineAsync(request.Token, request.Reason, ct);

        if (!success)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Invitation not found or cannot be declined"));
        }

        return NoContent();
    }

    /// <summary>
    /// Get pending invitations for the current user's email.
    /// </summary>
    [HttpGet("pending")]
    [ProducesResponseType(typeof(ApiResponse<List<CaInvitationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingInvitations(CancellationToken ct)
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
        {
            return Ok(new ApiResponse<List<CaInvitationDto>>(true, []));
        }

        var invitations = await _invitationService.GetPendingByEmailAsync(email, ct);
        return Ok(new ApiResponse<List<CaInvitationDto>>(true, invitations));
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }
}
