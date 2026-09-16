using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for managing CA profiles.
/// </summary>
public interface ICaProfileService
{
    /// <summary>
    /// Register a new CA user.
    /// </summary>
    Task<CaRegisterResult> RegisterCaAsync(CaRegisterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get CA profile by user ID.
    /// </summary>
    Task<CaProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Get CA profile DTO by user ID.
    /// </summary>
    Task<CaProfileDto?> GetProfileDtoAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Update CA profile.
    /// </summary>
    Task<CaProfile> UpdateAsync(Guid userId, UpdateCaProfileRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check if user is a CA.
    /// </summary>
    Task<bool> IsCaAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Create an organization for a CA user during onboarding.
    /// Unlike BO, GSTIN is optional for CA organizations.
    /// </summary>
    Task<CreateCaOrganizationResponse> CreateOrganizationAsync(
        Guid userId,
        CreateCaOrganizationRequest request,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default);
}

/// <summary>
/// Result of CA registration.
/// </summary>
public record CaRegisterResult(
    bool Success,
    Guid? UserId = null,
    Guid? CaProfileId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Service for managing CA invitations.
/// </summary>
public interface ICaInvitationService
{
    /// <summary>
    /// Create an invitation from CA to Business Owner.
    /// </summary>
    Task<CaInvitationResult> CreateInvitationAsync(
        Guid caUserId,
        CreateCaInvitationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Create an invitation from Business Owner to CA.
    /// </summary>
    Task<CaInvitationResult> CreateBoInvitationAsync(
        Guid boUserId,
        Guid organizationId,
        BoInviteCaRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get invitation by token hash.
    /// </summary>
    Task<CaInvitation?> GetByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Get pending invitations for an email.
    /// </summary>
    Task<List<CaInvitationDto>> GetPendingByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Get invitations sent by a CA.
    /// </summary>
    Task<CaInvitationListResponse> GetSentByCaAsync(
        Guid caUserId,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Accept an invitation.
    /// </summary>
    Task<AcceptInvitationResult> AcceptAsync(
        string token,
        Guid acceptingUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Decline an invitation.
    /// </summary>
    Task<bool> DeclineAsync(string token, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Cancel an invitation (by inviter).
    /// </summary>
    Task<bool> CancelAsync(Guid invitationId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Resend an invitation.
    /// </summary>
    Task<bool> ResendAsync(Guid invitationId, Guid caUserId, string? updatedMessage = null, CancellationToken ct = default);

    /// <summary>
    /// Validate invitation token without accepting.
    /// </summary>
    Task<CaInvitationDto?> ValidateTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Expire old pending invitations (background job).
    /// </summary>
    Task ExpirePendingInvitationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get daily invitation count for rate limiting.
    /// </summary>
    Task<int> GetDailyInvitationCountAsync(Guid caUserId, CancellationToken ct = default);
}

/// <summary>
/// Result of creating an invitation.
/// </summary>
public record CaInvitationResult(
    bool Success,
    Guid? InvitationId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Result of accepting an invitation.
/// </summary>
public record AcceptInvitationResult(
    bool Success,
    Guid? RelationshipId = null,
    string? Gstin = null,
    bool RequiresOrganizationSetup = false,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Service for managing CA-Client relationships.
/// </summary>
public interface ICaClientService
{
    /// <summary>
    /// Get all clients for a CA.
    /// </summary>
    Task<CaClientListResponse> GetClientsAsync(
        Guid caUserId,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Get a specific client relationship.
    /// </summary>
    Task<CaClientDto?> GetClientAsync(Guid caUserId, Guid relationshipId, CancellationToken ct = default);

    /// <summary>
    /// Get client relationship by CA and client user IDs.
    /// </summary>
    Task<CaClientRelationship?> GetRelationshipAsync(
        Guid caUserId,
        Guid clientUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Update client reference/notes.
    /// </summary>
    Task<bool> UpdateClientAsync(
        Guid caUserId,
        Guid relationshipId,
        UpdateCaClientRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Revoke CA access to a client (can be done by CA or BO).
    /// </summary>
    Task<bool> RevokeAsync(
        Guid relationshipId,
        Guid revokingUserId,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Link organization to a pending relationship.
    /// Called when BO creates/links their organization after accepting invitation.
    /// </summary>
    Task<bool> LinkOrganizationAsync(
        Guid clientUserId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Get CAs who have access to an organization.
    /// </summary>
    Task<List<CaClientDto>> GetCasForOrganizationAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Expire relationships past their expiry date (background job).
    /// </summary>
    Task ExpireRelationshipsAsync(CancellationToken ct = default);
}

/// <summary>
/// Service for managing CA GSTIN authorizations.
/// </summary>
public interface ICaAuthorizationService
{
    /// <summary>
    /// Check if CA has access to a specific GSTIN with required permission.
    /// </summary>
    Task<bool> HasAccessAsync(
        Guid caUserId,
        string gstin,
        string requiredPermission,
        CancellationToken ct = default);

    /// <summary>
    /// Get all authorizations for a CA.
    /// </summary>
    Task<List<CaGstinAuthorizationDto>> GetAuthorizationsAsync(
        Guid caUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Get authorizations for a specific client relationship.
    /// </summary>
    Task<List<CaGstinAuthorizationDto>> GetAuthorizationsByRelationshipAsync(
        Guid relationshipId,
        CancellationToken ct = default);

    /// <summary>
    /// Get authorization by GSTIN for a CA.
    /// </summary>
    Task<CaGstinAuthorization?> GetAuthorizationAsync(
        Guid caUserId,
        string gstin,
        CancellationToken ct = default);

    /// <summary>
    /// Update permissions for a GSTIN authorization (by BO).
    /// </summary>
    Task<bool> UpdatePermissionsAsync(
        Guid authorizationId,
        Guid boUserId,
        List<string> permissions,
        CancellationToken ct = default);

    /// <summary>
    /// Revoke authorization for a specific GSTIN.
    /// </summary>
    Task<bool> RevokeAsync(
        Guid authorizationId,
        Guid revokingUserId,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Link OrganizationGstin to pending authorizations.
    /// Called when GSTIN is claimed by BO.
    /// </summary>
    Task LinkOrganizationGstinAsync(
        string gstin,
        Guid organizationGstinId,
        CancellationToken ct = default);

    /// <summary>
    /// Get authorized GSTINs for a CA in a specific client context.
    /// </summary>
    Task<List<string>> GetAuthorizedGstinsAsync(
        Guid caUserId,
        Guid? organizationId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Service for managing CA client context.
/// </summary>
public interface ICaContextService
{
    /// <summary>
    /// Select a client context for the CA.
    /// </summary>
    Task<SelectClientContextResult> SelectClientAsync(
        Guid caUserId,
        Guid relationshipId,
        CancellationToken ct = default);

    /// <summary>
    /// Get current context for a CA.
    /// </summary>
    Task<CaContextDto?> GetCurrentContextAsync(Guid caUserId, CancellationToken ct = default);

    /// <summary>
    /// Clear the client context (back to client list).
    /// </summary>
    Task ClearContextAsync(Guid caUserId, CancellationToken ct = default);

    /// <summary>
    /// Validate that CA has active context for an organization.
    /// </summary>
    Task<bool> ValidateContextAsync(
        Guid caUserId,
        Guid organizationId,
        CancellationToken ct = default);
}

/// <summary>
/// Result of selecting client context.
/// </summary>
public record SelectClientContextResult(
    bool Success,
    string? AccessToken = null,
    string? RefreshToken = null,
    CaContextDto? Context = null,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Service for CA notice access.
/// </summary>
public interface ICaNoticeService
{
    /// <summary>
    /// Get notices for CA's selected client context.
    /// </summary>
    Task<CaNoticeListResponse> GetNoticesAsync(
        Guid caUserId,
        Guid organizationId,
        List<string> authorizedGstins,
        CaNoticeFilterDto filter,
        CancellationToken ct = default);

    /// <summary>
    /// Get notice detail for CA.
    /// </summary>
    Task<CaNoticeDetailDto?> GetNoticeAsync(
        Guid caUserId,
        Guid noticeId,
        Guid organizationId,
        List<string> authorizedGstins,
        CancellationToken ct = default);

    /// <summary>
    /// Get notice counts for dashboard.
    /// </summary>
    Task<(int total, int pending, int overdue)> GetNoticeCountsAsync(
        Guid organizationId,
        List<string> gstins,
        CancellationToken ct = default);
}

/// <summary>
/// Service for CA staging (pre-claim notice storage).
/// </summary>
public interface ICaStagingService
{
    /// <summary>
    /// Stage notices synced by CA before BO accepted invitation.
    /// </summary>
    Task<int> StageNoticesAsync(
        Guid caUserId,
        string gstin,
        Guid targetClientUserId,
        List<Notice> notices,
        CancellationToken ct = default);

    /// <summary>
    /// Transfer staged notices to BO's organization after acceptance.
    /// </summary>
    Task<int> TransferStagedNoticesAsync(
        Guid clientUserId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Get count of staged notices for an invitation.
    /// </summary>
    Task<int> GetStagedNoticeCountAsync(
        Guid caUserId,
        string gstin,
        Guid? targetClientUserId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Service for CA dashboard data.
/// </summary>
public interface ICaDashboardService
{
    /// <summary>
    /// Get CA dashboard overview.
    /// </summary>
    Task<CaDashboardDto> GetDashboardAsync(Guid caUserId, CancellationToken ct = default);

    /// <summary>
    /// Get dashboard for selected client.
    /// </summary>
    Task<CaClientDashboardDto> GetClientDashboardAsync(
        Guid caUserId,
        Guid relationshipId,
        CancellationToken ct = default);
}
