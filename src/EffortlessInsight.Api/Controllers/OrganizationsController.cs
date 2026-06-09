using System.Security.Claims;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

[ApiController]
[Route("api/v1/organizations")]
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationManagementService _organizationService;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly ILogger<OrganizationsController> _logger;

    public OrganizationsController(
        IOrganizationManagementService organizationService,
        ICurrentOrganizationService currentOrg,
        ILogger<OrganizationsController> logger)
    {
        _organizationService = organizationService;
        _currentOrg = currentOrg;
        _logger = logger;
    }

    #region Organization CRUD

    /// <summary>
    /// Create a new organization
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateOrganizationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.CreateAsync(request, userId);
            return StatusCode(StatusCodes.Status201Created, new ApiResponse<CreateOrganizationResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_GSTIN"))
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_GSTIN", ex.Message.Replace("INVALID_GSTIN: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message == "GSTIN_EXISTS")
        {
            return Conflict(new ApiErrorResponse(false, "GSTIN_EXISTS", "GSTIN is already registered with another organization"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "ORG_NAME_EXISTS")
        {
            return Conflict(new ApiErrorResponse(false, "ORG_NAME_EXISTS", "Organization name already exists"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create organization");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get all organizations the current user belongs to
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OrganizationListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.GetUserOrganizationsAsync(userId);
            return Ok(new ApiResponse<OrganizationListResponse>(true, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user organizations");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get organization details
    /// </summary>
    [HttpGet("{orgId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrganizationDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid orgId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.GetByIdAsync(orgId, userId);
            return Ok(new ApiResponse<OrganizationDetailResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "NOT_A_MEMBER")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_A_MEMBER", "You are not a member of this organization"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "ORGANIZATION_NOT_FOUND", "Organization not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Update organization details
    /// </summary>
    [HttpPut("{orgId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrganizationDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid orgId, [FromBody] UpdateOrganizationRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.UpdateAsync(orgId, request, userId);
            return Ok(new ApiResponse<OrganizationDetailResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "ORG_NAME_EXISTS")
        {
            return BadRequest(new ApiErrorResponse(false, "ORG_NAME_EXISTS", "Organization name already exists"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "ORGANIZATION_NOT_FOUND", "Organization not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Delete organization (soft delete)
    /// </summary>
    [HttpDelete("{orgId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid orgId, [FromBody] DeleteOrganizationRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _organizationService.DeleteAsync(orgId, request, userId);

            return Ok(new ApiResponse<object>(true, new
            {
                Message = "Organization scheduled for deletion",
                DeletionDate = DateTime.UtcNow.AddDays(30),
                CanRecoverUntil = DateTime.UtcNow.AddDays(30)
            }));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "OWNER_ONLY")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "OWNER_ONLY", "Only the owner can delete the organization"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_PASSWORD")
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_PASSWORD", "Password is incorrect"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CONFIRMATION_MISMATCH")
        {
            return BadRequest(new ApiErrorResponse(false, "CONFIRMATION_MISMATCH", "Confirmation does not match organization name"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "ORGANIZATION_NOT_FOUND", "Organization not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region GSTIN Management

    /// <summary>
    /// Validate GSTIN format and checksum (no auth required for validation only)
    /// </summary>
    [HttpGet("validate-gstin/{gstin}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<GstinValidationResult>), StatusCodes.Status200OK)]
    public IActionResult ValidateGstin(string gstin, [FromServices] IGstinValidatorService gstinValidator)
    {
        var result = gstinValidator.Validate(gstin);
        return Ok(new ApiResponse<GstinValidationResult>(true, result));
    }

    /// <summary>
    /// Add GSTIN to organization
    /// </summary>
    [HttpPost("{orgId:guid}/gstins")]
    [ProducesResponseType(typeof(ApiResponse<GstinDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddGstin(Guid orgId, [FromBody] AddGstinRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.AddGstinAsync(orgId, request, userId);
            return StatusCode(StatusCodes.Status201Created, new ApiResponse<GstinDto>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_GSTIN"))
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_GSTIN", ex.Message.Replace("INVALID_GSTIN: ", "")));
        }
        catch (InvalidOperationException ex) when (ex.Message == "GSTIN_EXISTS")
        {
            return Conflict(new ApiErrorResponse(false, "GSTIN_EXISTS", "GSTIN is already registered with another organization"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("GSTIN_LIMIT_EXCEEDED"))
        {
            return BadRequest(new ApiErrorResponse(false, "GSTIN_LIMIT_EXCEEDED", ex.Message.Replace("GSTIN_LIMIT_EXCEEDED: ", "")));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "ORGANIZATION_NOT_FOUND", "Organization not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add GSTIN to organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Remove GSTIN from organization
    /// </summary>
    [HttpDelete("{orgId:guid}/gstins/{gstinId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveGstin(Guid orgId, Guid gstinId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _organizationService.RemoveGstinAsync(orgId, gstinId, userId);
            return Ok(new ApiResponse<object>(true, new { Message = "GSTIN removed" }));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_DELETE_PRIMARY")
        {
            return BadRequest(new ApiErrorResponse(false, "CANNOT_DELETE_PRIMARY", "Cannot delete primary GSTIN. Change primary first."));
        }
        catch (InvalidOperationException ex) when (ex.Message == "GSTIN_HAS_NOTICES")
        {
            return BadRequest(new ApiErrorResponse(false, "GSTIN_HAS_NOTICES", "Cannot delete GSTIN with associated notices. Archive notices first."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove GSTIN {GstinId} from organization {OrgId}", gstinId, orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Set GSTIN as primary
    /// </summary>
    [HttpPut("{orgId:guid}/gstins/{gstinId:guid}/primary")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrimaryGstin(Guid orgId, Guid gstinId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _organizationService.SetPrimaryGstinAsync(orgId, gstinId, userId);
            return Ok(new ApiResponse<object>(true, new { Message = "Primary GSTIN updated" }));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set primary GSTIN {GstinId} for organization {OrgId}", gstinId, orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Member Management

    /// <summary>
    /// Get organization members
    /// </summary>
    [HttpGet("{orgId:guid}/members")]
    [ProducesResponseType(typeof(ApiResponse<MemberListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMembers(Guid orgId, [FromQuery] string? role, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.GetMembersAsync(orgId, userId, role, status, page, limit);
            return Ok(new ApiResponse<MemberListResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "NOT_A_MEMBER")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_A_MEMBER", "You are not a member of this organization"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "CA_CANNOT_VIEW_MEMBERS")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "CA_CANNOT_VIEW_MEMBERS", "CA role cannot view organization members"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get members for organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Change member role
    /// </summary>
    [HttpPut("{orgId:guid}/members/{memberId:guid}/role")]
    [ProducesResponseType(typeof(ApiResponse<ChangeMemberRoleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeMemberRole(Guid orgId, Guid memberId, [FromBody] ChangeMemberRoleRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.ChangeMemberRoleAsync(orgId, memberId, request, userId);
            return Ok(new ApiResponse<ChangeMemberRoleResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "NOT_A_MEMBER")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_A_MEMBER", "You are not a member of this organization"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_CANNOT_MODIFY_ADMIN")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_CANNOT_MODIFY_ADMIN", "Admin cannot modify another admin's role"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_CANNOT_PROMOTE_TO_ADMIN")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_CANNOT_PROMOTE_TO_ADMIN", "Admin cannot promote users to admin role"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "OWNER_ONLY")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "OWNER_ONLY", "Only the owner can perform this action"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_CHANGE_OWNER_ROLE")
        {
            return BadRequest(new ApiErrorResponse(false, "CANNOT_CHANGE_OWNER_ROLE", "Cannot change owner's role. Transfer ownership first."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "MEMBER_NOT_FOUND", "Member not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change role for member {MemberId} in organization {OrgId}", memberId, orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Remove member from organization
    /// </summary>
    [HttpDelete("{orgId:guid}/members/{memberId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid orgId, Guid memberId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _organizationService.RemoveMemberAsync(orgId, memberId, userId);
            return Ok(new ApiResponse<object>(true, new { Message = "Member removed from organization" }));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_CANNOT_REMOVE_ADMIN")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_CANNOT_REMOVE_ADMIN", "Admin cannot remove another admin"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_REMOVE_OWNER")
        {
            return BadRequest(new ApiErrorResponse(false, "CANNOT_REMOVE_OWNER", "Cannot remove owner. Transfer ownership first."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "MEMBER_NOT_FOUND", "Member not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove member {MemberId} from organization {OrgId}", memberId, orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Leave organization (self-removal)
    /// </summary>
    [HttpPost("{orgId:guid}/leave")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LeaveOrganization(Guid orgId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _organizationService.LeaveOrganizationAsync(orgId, userId);
            return Ok(new ApiResponse<object>(true, new { Message = "Successfully left organization" }));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "NOT_A_MEMBER")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_A_MEMBER", "You are not a member of this organization"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "OWNER_CANNOT_LEAVE")
        {
            return BadRequest(new ApiErrorResponse(false, "OWNER_CANNOT_LEAVE", "Owner cannot leave organization. Transfer ownership first."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Transfer organization ownership
    /// </summary>
    [HttpPost("{orgId:guid}/transfer-ownership")]
    [ProducesResponseType(typeof(ApiResponse<TransferOwnershipResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TransferOwnership(Guid orgId, [FromBody] TransferOwnershipRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.TransferOwnershipAsync(orgId, request, userId);
            return Ok(new ApiResponse<TransferOwnershipResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "OWNER_ONLY")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "OWNER_ONLY", "Only the owner can transfer ownership"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_PASSWORD")
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_PASSWORD", "Password is incorrect"));
        }
        catch (KeyNotFoundException ex) when (ex.Message == "NEW_OWNER_NOT_MEMBER")
        {
            return BadRequest(new ApiErrorResponse(false, "NEW_OWNER_NOT_MEMBER", "New owner must be an existing member of the organization"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Resource not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer ownership for organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Invitations

    /// <summary>
    /// Invite user to organization
    /// </summary>
    [HttpPost("{orgId:guid}/invitations")]
    [ProducesResponseType(typeof(ApiResponse<InvitationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InviteMember(Guid orgId, [FromBody] InviteMemberRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.InviteMemberAsync(orgId, request, userId);
            return StatusCode(StatusCodes.Status201Created, new ApiResponse<InvitationDto>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "CANNOT_INVITE_HIGHER_ROLE")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "CANNOT_INVITE_HIGHER_ROLE", "Admin cannot invite another admin or owner"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "USER_ALREADY_MEMBER")
        {
            return BadRequest(new ApiErrorResponse(false, "USER_ALREADY_MEMBER", "User is already a member of this organization"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITATION_PENDING")
        {
            return BadRequest(new ApiErrorResponse(false, "INVITATION_PENDING", "An invitation is already pending for this email"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("USER_LIMIT_EXCEEDED"))
        {
            return BadRequest(new ApiErrorResponse(false, "USER_LIMIT_EXCEEDED", ex.Message.Replace("USER_LIMIT_EXCEEDED: ", "")));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "ORGANIZATION_NOT_FOUND", "Organization not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invite member to organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get pending invitations
    /// </summary>
    [HttpGet("{orgId:guid}/invitations")]
    [ProducesResponseType(typeof(ApiResponse<InvitationListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvitations(Guid orgId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.GetInvitationsAsync(orgId, userId);
            return Ok(new ApiResponse<InvitationListResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get invitations for organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Resend invitation email
    /// </summary>
    [HttpPost("{orgId:guid}/invitations/{invitationId:guid}/resend")]
    [ProducesResponseType(typeof(ApiResponse<ResendInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvitation(Guid orgId, Guid invitationId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.ResendInvitationAsync(orgId, invitationId, userId);
            return Ok(new ApiResponse<ResendInvitationResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "MAX_RESENDS_EXCEEDED")
        {
            return BadRequest(new ApiErrorResponse(false, "MAX_RESENDS_EXCEEDED", "Maximum resend limit reached for this invitation"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "INVITATION_NOT_FOUND", "Invitation not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend invitation {InvitationId} for organization {OrgId}", invitationId, orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Cancel pending invitation
    /// </summary>
    [HttpDelete("{orgId:guid}/invitations/{invitationId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelInvitation(Guid orgId, Guid invitationId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _organizationService.CancelInvitationAsync(orgId, invitationId, userId);
            return Ok(new ApiResponse<object>(true, new { Message = "Invitation cancelled" }));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "INVITATION_NOT_FOUND", "Invitation not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel invitation {InvitationId} for organization {OrgId}", invitationId, orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Settings

    /// <summary>
    /// Update organization settings
    /// </summary>
    [HttpPut("{orgId:guid}/settings")]
    [ProducesResponseType(typeof(ApiResponse<OrganizationSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSettings(Guid orgId, [FromBody] UpdateOrganizationSettingsRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.UpdateSettingsAsync(orgId, request, userId);
            return Ok(new ApiResponse<OrganizationSettingsDto>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ADMIN_REQUIRED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ADMIN_REQUIRED", "Admin or owner access required"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "ORGANIZATION_NOT_FOUND", "Organization not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update settings for organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Private Methods

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        return Guid.Parse(userIdClaim);
    }

    private string GetClientIpAddress()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    #endregion
}

/// <summary>
/// Standalone invitation acceptance endpoint (not under organization path)
/// </summary>
[ApiController]
[Route("api/v1/invitations")]
[Authorize]
public class InvitationsController : ControllerBase
{
    private readonly IOrganizationManagementService _organizationService;
    private readonly ILogger<InvitationsController> _logger;

    public InvitationsController(
        IOrganizationManagementService organizationService,
        ILogger<InvitationsController> logger)
    {
        _organizationService = organizationService;
        _logger = logger;
    }

    /// <summary>
    /// Accept organization invitation
    /// </summary>
    [HttpPost("{token}/accept")]
    [ProducesResponseType(typeof(ApiResponse<AcceptInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptInvitation(string token)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _organizationService.AcceptInvitationAsync(token, userId);
            return Ok(new ApiResponse<AcceptInvitationResponse>(true, result));
        }
        catch (KeyNotFoundException ex) when (ex.Message == "INVALID_INVITATION")
        {
            return NotFound(new ApiErrorResponse(false, "INVALID_INVITATION", "Invitation not found or invalid"));
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
        catch (InvalidOperationException ex) when (ex.Message == "ALREADY_MEMBER")
        {
            return BadRequest(new ApiErrorResponse(false, "ALREADY_MEMBER", "You are already a member of this organization"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept invitation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Decline organization invitation
    /// </summary>
    [HttpPost("{token}/decline")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineInvitation(string token)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _organizationService.DeclineInvitationAsync(token, userId);
            return Ok(new ApiResponse<object>(true, new { Message = "Invitation declined" }));
        }
        catch (KeyNotFoundException ex) when (ex.Message == "INVALID_INVITATION")
        {
            return NotFound(new ApiErrorResponse(false, "INVALID_INVITATION", "Invitation not found or invalid"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVITATION_"))
        {
            return BadRequest(new ApiErrorResponse(false, ex.Message, $"Invitation is {ex.Message.Replace("INVITATION_", "").ToLowerInvariant()}"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_MISMATCH")
        {
            return BadRequest(new ApiErrorResponse(false, "EMAIL_MISMATCH", "Logged in email does not match invitation"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decline invitation");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
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
