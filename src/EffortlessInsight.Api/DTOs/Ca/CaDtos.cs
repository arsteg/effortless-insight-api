using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.DTOs.Ca;

// ============================================================================
// CA Profile DTOs
// ============================================================================

/// <summary>
/// Request to register as a Chartered Accountant.
/// </summary>
public record CaRegisterRequest(
    [MaxLength(255)] string Email,
    [MaxLength(128)] string Password,
    [MaxLength(100)] string Name,
    [MaxLength(20)] string? Mobile,
    bool AcceptTerms,
    [MaxLength(255)] string? FirmName = null,
    [MaxLength(50)] string? MembershipNumber = null,
    [MaxLength(128)] string? MobileVerificationToken = null
);

/// <summary>
/// Response after CA registration.
/// </summary>
public record CaRegisterResponse(
    Guid UserId,
    Guid CaProfileId,
    string Email,
    string Name,
    bool EmailVerified,
    string Message
);

/// <summary>
/// CA profile information.
/// </summary>
public record CaProfileDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string UserEmail,
    string? FirmName,
    string? MembershipNumber,
    bool IsVerified,
    DateTime? VerifiedAt,
    string Status,
    DateTime CreatedAt,
    int ActiveClientCount,
    int PendingInvitationCount
);

/// <summary>
/// Request to update CA profile.
/// </summary>
public record UpdateCaProfileRequest(
    [MaxLength(255)] string? FirmName = null,
    [MaxLength(50)] string? MembershipNumber = null
);

// ============================================================================
// CA Invitation DTOs
// ============================================================================

/// <summary>
/// Request to send a CA invitation to a Business Owner.
/// </summary>
public record CreateCaInvitationRequest(
    [Required]
    [MaxLength(255)]
    string Email,

    [Required]
    [MaxLength(15)]
    [RegularExpression(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$",
        ErrorMessage = "Invalid GSTIN format")]
    string Gstin,

    [MaxLength(1000)]
    string? Message = null
);

/// <summary>
/// Response after creating an invitation.
/// </summary>
public record CreateCaInvitationResponse(
    Guid InvitationId,
    string InviteeEmail,
    string Gstin,
    string Status,
    DateTime ExpiresAt,
    string Message
);

/// <summary>
/// CA invitation details.
/// </summary>
public record CaInvitationDto(
    Guid Id,
    string InvitationType,
    string InviteeEmail,
    string Gstin,
    string Status,
    DateTime ExpiresAt,
    DateTime? RespondedAt,
    string? Message,
    int SendCount,
    DateTime LastSentAt,
    int StagedNoticeCount,
    DateTime CreatedAt,
    // For BO perspective
    CaInviterDto? Inviter = null,
    // For CA perspective
    AcceptedUserDto? AcceptedUser = null
);

/// <summary>
/// CA inviter info (shown to Business Owner).
/// </summary>
public record CaInviterDto(
    Guid UserId,
    string Name,
    string Email,
    string? FirmName,
    bool IsVerified
);

/// <summary>
/// Info about user who accepted invitation.
/// </summary>
public record AcceptedUserDto(
    Guid UserId,
    string Name,
    string Email
);

/// <summary>
/// Request to resend an invitation.
/// </summary>
public record ResendCaInvitationRequest(
    [MaxLength(1000)] string? UpdatedMessage = null
);

/// <summary>
/// Request from BO to invite a CA.
/// </summary>
public record BoInviteCaRequest(
    [Required]
    [MaxLength(255)]
    string CaEmail,

    [Required]
    List<string> Gstins,

    [MaxLength(1000)]
    string? Message = null
);

/// <summary>
/// Request to accept an invitation.
/// </summary>
public record AcceptCaInvitationRequest(
    [Required]
    [MaxLength(500)]
    string Token
);

/// <summary>
/// Response after accepting an invitation.
/// </summary>
public record AcceptCaInvitationResponse(
    Guid RelationshipId,
    string Gstin,
    string Message,
    bool RequiresOrganizationSetup
);

/// <summary>
/// Request to decline an invitation.
/// </summary>
public record DeclineCaInvitationRequest(
    [Required]
    [MaxLength(500)]
    string Token,

    [MaxLength(500)]
    string? Reason = null
);

// ============================================================================
// CA Client Relationship DTOs
// ============================================================================

/// <summary>
/// CA's client (Business Owner) summary.
/// </summary>
public record CaClientDto(
    Guid RelationshipId,
    Guid ClientUserId,
    string ClientName,
    string ClientEmail,
    Guid? OrganizationId,
    string? OrganizationName,
    string Status,
    DateTime InvitedAt,
    DateTime? AcceptedAt,
    string? ClientReference,
    List<CaGstinAuthorizationDto> GstinAuthorizations,
    int TotalNoticeCount,
    int PendingNoticeCount,
    DateTime? LastSyncAt
);

/// <summary>
/// Brief client summary for list views.
/// </summary>
public record CaClientSummaryDto(
    Guid RelationshipId,
    Guid ClientUserId,
    string ClientName,
    string ClientEmail,
    // Null until the Business Owner creates their organization. The client picker uses
    // this to disable clients there is nothing to switch into yet.
    Guid? OrganizationId,
    string? OrganizationName,
    string Status,
    int AuthorizedGstinCount,
    int TotalNoticeCount,
    int PendingNoticeCount,
    DateTime? LastSyncAt
);

/// <summary>
/// Request to update client reference.
/// </summary>
public record UpdateCaClientRequest(
    [MaxLength(100)] string? ClientReference = null,
    [MaxLength(1000)] string? Notes = null
);

/// <summary>
/// Request to revoke CA-client relationship.
/// </summary>
public record RevokeCaRelationshipRequest(
    [MaxLength(500)] string? Reason = null
);

// ============================================================================
// CA GSTIN Authorization DTOs
// ============================================================================

/// <summary>
/// GSTIN authorization details.
/// </summary>
public record CaGstinAuthorizationDto(
    Guid Id,
    string Gstin,
    Guid? OrganizationGstinId,
    string? TradeName,
    string? LegalName,
    string? StateCode,
    string? StateName,
    string Status,
    List<string> Permissions,
    DateTime GrantedAt,
    DateTime? LastSyncAt,
    int NoticeCount
);

/// <summary>
/// Request to update GSTIN permissions.
/// </summary>
public record UpdateCaGstinPermissionsRequest(
    [Required]
    List<string> Permissions
);

/// <summary>
/// Request to revoke GSTIN authorization.
/// </summary>
public record RevokeCaGstinAuthorizationRequest(
    [MaxLength(500)] string? Reason = null
);

// ============================================================================
// CA Context DTOs
// ============================================================================

/// <summary>
/// Request to select a client context.
/// </summary>
public record SelectClientContextRequest(
    [Required]
    Guid ClientRelationshipId
);

/// <summary>
/// Current CA context information.
/// </summary>
public record CaContextDto(
    Guid CaUserId,
    string CaName,
    Guid? SelectedClientRelationshipId,
    Guid? SelectedOrganizationId,
    string? SelectedClientName,
    string? SelectedOrganizationName,
    List<string> AuthorizedGstins,
    List<string> Permissions,
    DateTime? ContextSetAt
);

/// <summary>
/// Response after selecting client context (includes new JWT).
/// </summary>
public record SelectClientContextResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    CaContextDto Context
);

// ============================================================================
// CA Dashboard DTOs
// ============================================================================

/// <summary>
/// CA dashboard overview.
/// </summary>
public record CaDashboardDto(
    int TotalClients,
    int ActiveClients,
    int PendingInvitations,
    int TotalAuthorizedGstins,
    int TotalNoticeCount,
    int PendingNoticeCount,
    int NoticesTodayCount,
    List<CaDashboardClientSummary> RecentClients,
    List<CaDashboardNoticeSummary> RecentNotices
);

/// <summary>
/// Client summary for dashboard.
/// </summary>
public record CaDashboardClientSummary(
    Guid RelationshipId,
    string ClientName,
    string? OrganizationName,
    int NoticeCount,
    int PendingCount,
    DateTime? LastActivity
);

/// <summary>
/// Notice summary for dashboard.
/// </summary>
public record CaDashboardNoticeSummary(
    Guid NoticeId,
    string? NoticeNumber,
    string? NoticeType,
    string Gstin,
    string ClientName,
    string Status,
    DateOnly? ResponseDeadline,
    DateTime CreatedAt
);

/// <summary>
/// Client dashboard for selected client context.
/// </summary>
public record CaClientDashboardDto(
    Guid ClientRelationshipId,
    string ClientName,
    string? OrganizationName,
    List<CaGstinAuthorizationDto> AuthorizedGstins,
    int TotalNoticeCount,
    int PendingNoticeCount,
    int OverdueNoticeCount,
    List<CaDashboardNoticeSummary> RecentNotices,
    List<CaActivitySummary> RecentActivity
);

/// <summary>
/// Recent activity item.
/// </summary>
public record CaActivitySummary(
    string ActivityType,
    string Description,
    string? EntityType,
    Guid? EntityId,
    DateTime CreatedAt
);

// ============================================================================
// CA Notice DTOs
// ============================================================================

/// <summary>
/// Notice as viewed by CA (subset of full notice).
/// </summary>
public record CaNoticeDto(
    Guid Id,
    string? NoticeNumber,
    string? NoticeType,
    string? NoticeCategory,
    string Gstin,
    string? TradeName,
    DateOnly? IssueDate,
    DateOnly? ResponseDeadline,
    decimal? TotalDemand,
    string Status,
    string Priority,
    string? Summary,
    string? IssuingAuthority,
    string Source,
    DateTime CreatedAt,
    Guid? CaSyncedByUserId,
    bool IsStagedByMe
);

/// <summary>
/// Notice detail as viewed by CA.
/// </summary>
public record CaNoticeDetailDto(
    Guid Id,
    string? NoticeNumber,
    string? NoticeType,
    string? NoticeCategory,
    string? NoticeSubCategory,
    string Gstin,
    string? TradeName,
    DateOnly? IssueDate,
    DateOnly? ResponseDeadline,
    DateOnly? ExtendedDeadline,
    DateOnly? HearingDate,
    decimal? TaxAmount,
    decimal? PenaltyAmount,
    decimal? InterestAmount,
    decimal? TotalDemand,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? FinancialYear,
    string? IssuingAuthority,
    string? IssuingOfficer,
    string? OfficerDesignation,
    string? Jurisdiction,
    string Status,
    string Priority,
    string? Summary,
    string? Section,
    string FileUrl,
    string FileName,
    string Source,
    List<string>? Tags,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    // Permissions for this notice
    bool CanComment,
    bool CanDraftResponse
);

/// <summary>
/// Filter for CA notice list.
/// </summary>
public record CaNoticeFilterDto(
    string? Gstin = null,
    string? Status = null,
    string? Priority = null,
    string? NoticeType = null,
    DateOnly? IssueDateFrom = null,
    DateOnly? IssueDateTo = null,
    DateOnly? DeadlineFrom = null,
    DateOnly? DeadlineTo = null,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "createdAt",
    bool SortDescending = true
);

/// <summary>
/// Paged list of notices.
/// </summary>
public record CaNoticeListResponse(
    List<CaNoticeDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages
);

// ============================================================================
// CA Sync DTOs
// ============================================================================

/// <summary>
/// Request to start sync for a GSTIN.
/// </summary>
public record CaStartSyncRequest(
    [Required]
    [MaxLength(15)]
    string Gstin
);

/// <summary>
/// Sync session summary.
/// </summary>
public record CaSyncSessionDto(
    Guid Id,
    string Gstin,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int NoticesFound,
    int NoticesImported,
    int NoticesSkipped,
    string? ErrorMessage
);

// ============================================================================
// Paged Response Helpers
// ============================================================================

public record CaClientListResponse(
    List<CaClientSummaryDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages
);

public record CaInvitationListResponse(
    List<CaInvitationDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages
);
