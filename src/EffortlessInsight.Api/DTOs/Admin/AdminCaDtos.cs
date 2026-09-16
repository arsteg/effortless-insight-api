using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.DTOs.Admin;

// ============================================================================
// Admin CA Profile DTOs
// ============================================================================

/// <summary>
/// CA profile list item for admin view.
/// </summary>
public record AdminCaProfileListItemDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string UserEmail,
    string? FirmName,
    string? MembershipNumber,
    bool IsVerified,
    DateTime? VerifiedAt,
    string Status,
    int ActiveClientCount,
    int PendingInvitationCount,
    DateTime CreatedAt
);

/// <summary>
/// Detailed CA profile for admin view.
/// </summary>
public record AdminCaProfileDetailDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string UserEmail,
    string? UserMobile,
    string? FirmName,
    string? MembershipNumber,
    bool IsVerified,
    DateTime? VerifiedAt,
    Guid? VerifiedByAdminId,
    string? VerifiedByAdminName,
    string Status,
    DateTime? SuspendedAt,
    Guid? SuspendedByAdminId,
    string? SuspendedReason,
    int ActiveClientCount,
    int PendingInvitationCount,
    int TotalAuthorizedGstins,
    List<AdminCaRelationshipSummaryDto> Relationships,
    List<AdminCaInvitationSummaryDto> RecentInvitations,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

/// <summary>
/// CA relationship summary for profile detail.
/// </summary>
public record AdminCaRelationshipSummaryDto(
    Guid Id,
    Guid ClientUserId,
    string ClientUserName,
    string ClientUserEmail,
    Guid? OrganizationId,
    string? OrganizationName,
    string Status,
    int GstinCount,
    int NoticeCount,
    DateTime InvitedAt,
    DateTime? AcceptedAt
);

/// <summary>
/// CA invitation summary for profile detail.
/// </summary>
public record AdminCaInvitationSummaryDto(
    Guid Id,
    string InviteeEmail,
    string Gstin,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RespondedAt
);

/// <summary>
/// Search parameters for CA list.
/// </summary>
public record AdminCaSearchParams
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public bool? IsVerified { get; init; }
    public string? SortBy { get; init; } = "createdAt";
    public bool SortDesc { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>
/// Paginated list of CAs for admin.
/// </summary>
public record AdminCaListResponse(
    List<AdminCaProfileListItemDto> Cas,
    AdminPaginationDto Pagination
);

// ============================================================================
// Admin CA Relationship DTOs
// ============================================================================

/// <summary>
/// CA-Client relationship list item for admin view.
/// </summary>
public record AdminCaRelationshipListItemDto(
    Guid Id,
    Guid CaUserId,
    string CaUserName,
    string CaUserEmail,
    string? CaFirmName,
    Guid ClientUserId,
    string ClientUserName,
    string ClientUserEmail,
    Guid? OrganizationId,
    string? OrganizationName,
    string Status,
    int GstinCount,
    int NoticeCount,
    DateTime InvitedAt,
    DateTime? AcceptedAt,
    DateTime? RevokedAt
);

/// <summary>
/// Detailed CA-Client relationship for admin view.
/// </summary>
public record AdminCaRelationshipDetailDto(
    Guid Id,
    AdminCaProfileBriefDto CaProfile,
    AdminClientBriefDto Client,
    string Status,
    List<AdminCaGstinAuthorizationDto> GstinAuthorizations,
    DateTime InvitedAt,
    DateTime? AcceptedAt,
    DateTime? RevokedAt,
    string? RevokedBy,
    string? RevokeReason,
    int NoticeCount,
    DateTime? LastActivityAt
);

/// <summary>
/// Brief CA profile for relationship detail.
/// </summary>
public record AdminCaProfileBriefDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string UserEmail,
    string? FirmName,
    bool IsVerified
);

/// <summary>
/// Brief client info for relationship detail.
/// </summary>
public record AdminClientBriefDto(
    Guid UserId,
    string UserName,
    string UserEmail,
    Guid? OrganizationId,
    string? OrganizationName
);

/// <summary>
/// GSTIN authorization for admin view.
/// </summary>
public record AdminCaGstinAuthorizationDto(
    Guid Id,
    string Gstin,
    string? TradeName,
    string? LegalName,
    string? StateCode,
    string? StateName,
    string Status,
    List<string> Permissions,
    DateTime GrantedAt,
    DateTime? RevokedAt,
    int NoticeCount
);

/// <summary>
/// Search parameters for CA relationships.
/// </summary>
public record AdminCaRelationshipSearchParams
{
    public string? Search { get; init; }
    public Guid? CaUserId { get; init; }
    public Guid? ClientUserId { get; init; }
    public Guid? OrganizationId { get; init; }
    public string? Status { get; init; }
    public string? SortBy { get; init; } = "invitedAt";
    public bool SortDesc { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>
/// Paginated list of CA relationships.
/// </summary>
public record AdminCaRelationshipListResponse(
    List<AdminCaRelationshipListItemDto> Relationships,
    AdminPaginationDto Pagination
);

// ============================================================================
// Admin CA Invitation DTOs
// ============================================================================

/// <summary>
/// CA invitation list item for admin view.
/// </summary>
public record AdminCaInvitationListItemDto(
    Guid Id,
    string InvitationType,
    Guid CaUserId,
    string CaUserName,
    string CaUserEmail,
    string? CaFirmName,
    string InviteeEmail,
    string Gstin,
    string Status,
    int SendCount,
    DateTime LastSentAt,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RespondedAt,
    Guid? AcceptedUserId,
    string? AcceptedUserName
);

/// <summary>
/// Search parameters for CA invitations.
/// </summary>
public record AdminCaInvitationSearchParams
{
    public string? Search { get; init; }
    public Guid? CaUserId { get; init; }
    public string? Status { get; init; }
    public string? SortBy { get; init; } = "createdAt";
    public bool SortDesc { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

/// <summary>
/// Paginated list of CA invitations.
/// </summary>
public record AdminCaInvitationListResponse(
    List<AdminCaInvitationListItemDto> Invitations,
    AdminPaginationDto Pagination
);

// ============================================================================
// Admin CA Action Request DTOs
// ============================================================================

/// <summary>
/// Request to verify a CA.
/// </summary>
public record AdminVerifyCaRequest
{
    public bool MembershipVerified { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

/// <summary>
/// Request to revoke CA verification.
/// </summary>
public record AdminRevokeCaVerificationRequest
{
    [MaxLength(500)]
    public string? Reason { get; init; }
}

/// <summary>
/// Request to suspend a CA.
/// </summary>
public record AdminSuspendCaRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

/// <summary>
/// Request to revoke a CA-Client relationship.
/// </summary>
public record AdminRevokeCaRelationshipRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Request to cancel a CA invitation.
/// </summary>
public record AdminCancelCaInvitationRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

// ============================================================================
// Common Admin DTOs
// ============================================================================

/// <summary>
/// Standard pagination info.
/// </summary>
public record AdminPaginationDto(
    int Page,
    int PageSize,
    int TotalRecords,
    int TotalPages
);
