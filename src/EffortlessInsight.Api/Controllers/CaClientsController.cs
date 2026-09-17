using System.Security.Claims;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// CA-as-distributor endpoints: a self-registered CA (ApplicationUser.IsCA) manages
/// their client roster here - inviting Business Owners by GSTIN before those BOs
/// have an account, and staging notices for a client ahead of acceptance. Once a BO
/// accepts (see CaClientInvitationsController), the CA operates through the exact
/// same modules everyone else uses (NoticesController etc.), scoped to that BO's
/// organization via the existing /auth/switch-organization flow.
/// </summary>
[ApiController]
[Route("api/v1/ca/clients")]
[Authorize]
public class CaClientsController : ControllerBase
{
    private readonly ICaClientService _caClientService;
    private readonly ILogger<CaClientsController> _logger;

    public CaClientsController(ICaClientService caClientService, ILogger<CaClientsController> logger)
    {
        _caClientService = caClientService;
        _logger = logger;
    }

    /// <summary>
    /// Invite a Business Owner to claim/create an organization for a GSTIN.
    /// </summary>
    [HttpPost("invitations")]
    [ProducesResponseType(typeof(ApiResponse<CaClientInvitationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvitation([FromBody] CreateCaClientInvitationRequest request)
    {
        try
        {
            var caUserId = GetCurrentUserId();
            var result = await _caClientService.CreateInvitationAsync(caUserId, request);
            return StatusCode(StatusCodes.Status201Created, new ApiResponse<CaClientInvitationDto>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "NOT_A_CA")
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("CA_ORGANIZATION_NOT_FOUND"))
        {
            return BadRequest(new ApiErrorResponse(false, "CA_ORGANIZATION_NOT_FOUND", ex.Message.Replace("CA_ORGANIZATION_NOT_FOUND: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_GSTIN"))
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_GSTIN", ex.Message.Replace("INVALID_GSTIN: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("GSTIN_HAS_ACTIVE_CA"))
        {
            return Conflict(new ApiErrorResponse(false, "GSTIN_HAS_ACTIVE_CA", ex.Message.Replace("GSTIN_HAS_ACTIVE_CA: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITATION_PENDING")
        {
            return Conflict(new ApiErrorResponse(false, "INVITATION_PENDING", "An invitation for this GSTIN is already pending"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "PROSPECT_CLIENT_NOT_STAGING")
        {
            return Conflict(new ApiErrorResponse(false, "PROSPECT_CLIENT_NOT_STAGING", "This client has already been claimed or reassigned"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", "User not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create CA client invitation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Resolve invitation details for the acceptance page. Anonymous - the token
    /// itself is the secret, same trust model as the existing invitation flow.
    /// </summary>
    [HttpGet("invitations/{token}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CaClientInvitationDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvitation(string token)
    {
        try
        {
            var result = await _caClientService.GetInvitationByTokenAsync(token);
            return Ok(new ApiResponse<CaClientInvitationDetailsDto>(true, result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "INVALID_INVITATION", "Invitation not found or invalid"));
        }
    }

    /// <summary>
    /// Accept a CA client invitation: creates a new organization for the GSTIN
    /// captured in the invitation (not re-entered here), adds the inviting CA as
    /// a role="ca" member, and merges any notices the CA staged pre-acceptance.
    /// Requires the caller to already be authenticated as the invited email -
    /// if they don't have an account yet, they must register/verify/login first.
    /// </summary>
    [HttpPost("invitations/{token}/accept")]
    [ProducesResponseType(typeof(ApiResponse<AcceptCaClientInvitationResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptInvitation(string token, [FromBody] AcceptCaClientInvitationRequest request)
    {
        try
        {
            var boUserId = GetCurrentUserId();
            var result = await _caClientService.AcceptInvitationAsync(token, boUserId, request);
            return Ok(new ApiResponse<AcceptCaClientInvitationResult>(true, result));
        }
        catch (KeyNotFoundException ex) when (ex.Message == "INVALID_INVITATION")
        {
            return NotFound(new ApiErrorResponse(false, "INVALID_INVITATION", "Invitation not found or invalid"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", "User not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITATION_EXPIRED")
        {
            return BadRequest(new ApiErrorResponse(false, "INVITATION_EXPIRED", "Invitation has expired"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVITATION_"))
        {
            return BadRequest(new ApiErrorResponse(false, ex.Message, $"Invitation is {ex.Message.Replace("INVITATION_", "").ToLowerInvariant()}"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_MISMATCH")
        {
            return BadRequest(new ApiErrorResponse(false, "EMAIL_MISMATCH", "Logged in email does not match invitation"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("GSTIN_ALREADY_CLAIMED"))
        {
            return Conflict(new ApiErrorResponse(false, "GSTIN_ALREADY_CLAIMED", ex.Message.Replace("GSTIN_ALREADY_CLAIMED: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message == "GSTIN_EXISTS")
        {
            return Conflict(new ApiErrorResponse(false, "GSTIN_EXISTS", "GSTIN is already registered with another organization"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_GSTIN"))
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_GSTIN", ex.Message.Replace("INVALID_GSTIN: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message == "ORG_NAME_EXISTS")
        {
            return Conflict(new ApiErrorResponse(false, "ORG_NAME_EXISTS", "Organization name already exists"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ORGANIZATION_LIMIT_EXCEEDED"))
        {
            return BadRequest(new ApiErrorResponse(false, "ORGANIZATION_LIMIT_EXCEEDED", ex.Message.Replace("ORGANIZATION_LIMIT_EXCEEDED: ", "")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept CA client invitation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Decline a CA client invitation. Staged data is kept - the CA may still
    /// hold it and re-invite later.
    /// </summary>
    [HttpPost("invitations/{token}/decline")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineClientInvitation(string token)
    {
        try
        {
            var boUserId = GetCurrentUserId();
            await _caClientService.DeclineInvitationAsync(token, boUserId);
            return Ok(new ApiResponse<object>(true, new { Message = "Invitation declined" }));
        }
        catch (KeyNotFoundException ex) when (ex.Message == "INVALID_INVITATION")
        {
            return NotFound(new ApiErrorResponse(false, "INVALID_INVITATION", "Invitation not found or invalid"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", "User not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVITATION_"))
        {
            return BadRequest(new ApiErrorResponse(false, ex.Message, $"Invitation is {ex.Message.Replace("INVITATION_", "").ToLowerInvariant()}"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_MISMATCH")
        {
            return BadRequest(new ApiErrorResponse(false, "EMAIL_MISMATCH", "Logged in email does not match invitation"));
        }
    }

    /// <summary>
    /// Resend a pending invitation with a freshly minted link (the previous link
    /// stops working, matching the existing OrganizationInvitation resend behavior).
    /// </summary>
    [HttpPost("invitations/{invitationId:guid}/resend")]
    [ProducesResponseType(typeof(ApiResponse<CaClientInvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendInvitation(Guid invitationId)
    {
        try
        {
            var caUserId = GetCurrentUserId();
            var result = await _caClientService.ResendInvitationAsync(caUserId, invitationId);
            return Ok(new ApiResponse<CaClientInvitationDto>(true, result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "INVITATION_NOT_FOUND", "Invitation not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "MAX_RESENDS_EXCEEDED")
        {
            return BadRequest(new ApiErrorResponse(false, "MAX_RESENDS_EXCEEDED", "Maximum number of resends reached"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVITATION_"))
        {
            return BadRequest(new ApiErrorResponse(false, ex.Message, $"Invitation is {ex.Message.Replace("INVITATION_", "").ToLowerInvariant()}"));
        }
    }

    /// <summary>
    /// Cancel a pending invitation.
    /// </summary>
    [HttpDelete("invitations/{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelInvitation(Guid invitationId)
    {
        try
        {
            var caUserId = GetCurrentUserId();
            await _caClientService.CancelInvitationAsync(caUserId, invitationId);
            return Ok(new ApiResponse<object?>(true, new { Message = "Invitation cancelled" }));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "INVITATION_NOT_FOUND", "Invitation not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVITATION_"))
        {
            return BadRequest(new ApiErrorResponse(false, ex.Message, $"Invitation is {ex.Message.Replace("INVITATION_", "").ToLowerInvariant()}"));
        }
    }

    /// <summary>
    /// Unified list of this CA's clients - both already-accepted ("active",
    /// backed by a real role="ca" organization membership) and pre-acceptance
    /// ("staged", backed by a CaProspectClient with no organization yet).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<CaClientListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClients()
    {
        var caUserId = GetCurrentUserId();
        var result = await _caClientService.GetClientsAsync(caUserId);
        return Ok(new ApiResponse<List<CaClientListItemDto>>(true, result));
    }

    /// <summary>
    /// Manually upload a notice for a staged (pre-acceptance) client. Stored as a
    /// CaStagedNotice, not a Notice - it is merged into the BO's real Notices once
    /// they accept the invitation (see CaClientInvitationsController.Accept).
    /// </summary>
    [HttpPost("{prospectClientId:guid}/notices/upload")]
    [RequestSizeLimit(26_214_400)] // 25 MB, matches NoticesController.Upload
    [ProducesResponseType(typeof(ApiResponse<UploadCaStagedNoticeResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadStagedNotice(
        Guid prospectClientId,
        [FromForm] UploadCaStagedNoticeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(new ApiErrorResponse(false, "FILE_REQUIRED", "No file was uploaded"));
        }

        try
        {
            var caUserId = GetCurrentUserId();
            using var stream = request.File.OpenReadStream();
            var result = await _caClientService.UploadStagedNoticeAsync(
                caUserId, prospectClientId, stream, request.File.FileName, request.File.ContentType, cancellationToken);

            return StatusCode(StatusCodes.Status202Accepted, new ApiResponse<UploadCaStagedNoticeResult>(true, result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "PROSPECT_CLIENT_NOT_FOUND", "Client not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "PROSPECT_CLIENT_NOT_STAGING")
        {
            return Conflict(new ApiErrorResponse(false, "PROSPECT_CLIENT_NOT_STAGING", "This client has already been claimed or reassigned"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("GSTIN_HAS_ACTIVE_CA"))
        {
            return Conflict(new ApiErrorResponse(false, "GSTIN_HAS_ACTIVE_CA", ex.Message.Replace("GSTIN_HAS_ACTIVE_CA: ", "")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload staged notice for prospect client {ProspectClientId}", prospectClientId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "UPLOAD_FAILED", "An unexpected error occurred during upload"));
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        return Guid.Parse(userIdClaim);
    }
}

public record UploadCaStagedNoticeRequest
{
    public IFormFile? File { get; init; }
}
