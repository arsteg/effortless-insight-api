using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// Auth DTOs
// ============================================================================

public record RegisterRequest(
    [MaxLength(255)] string Email,
    [MaxLength(128)] string Password,
    [MaxLength(100)] string Name,
    [MaxLength(20)] string? Mobile,
    bool AcceptTerms
);

public record RegisterResponse(
    Guid UserId,
    string Email,
    string Name,
    bool EmailVerified,
    string Message
);

public record LoginRequest(
    [MaxLength(255)] string Email,
    [MaxLength(128)] string Password,
    bool RememberMe = false,
    DeviceInfo? DeviceInfo = null
);

public record DeviceInfo(
    [MaxLength(100)] string? DeviceId,
    [MaxLength(100)] string? DeviceName,
    [MaxLength(20)] string Platform // web, ios, android
);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    UserDto User,
    string? MobileRedirectUri = null
);

public record TwoFactorRequiredResponse(
    bool Requires2fa,
    string PartialToken,
    int ExpiresIn,
    List<string> Methods
);

public record VerifyEmailRequest([MaxLength(500)] string Token);

public record RefreshTokenRequest([MaxLength(2000)] string RefreshToken);

public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn
);

public record ForgotPasswordRequest([MaxLength(255)] string Email);

public record ResetPasswordRequest(
    [MaxLength(500)] string Token,
    [MaxLength(128)] string Password,
    [MaxLength(128)] string ConfirmPassword
);

public record ChangePasswordRequest(
    [MaxLength(128)] string CurrentPassword,
    [MaxLength(128)] string NewPassword,
    [MaxLength(128)] string ConfirmPassword
);

public record LogoutRequest(bool AllDevices = false);

// OTP DTOs
public record OtpRequestRequest([MaxLength(20)] string Mobile, [MaxLength(20)] string Purpose = "login");
public record OtpResponse(string Message, string MaskedMobile, int ExpiresIn, int RetryAfter);
public record OtpVerifyRequest([MaxLength(20)] string Mobile, [MaxLength(10)] string Otp);

// 2FA Setup DTOs
public record TwoFactorSetupResponse(string Secret, string QrCodeDataUrl, string OtpauthUrl, List<string> BackupCodes);
public record TwoFactorVerifySetupRequest([MaxLength(10)] string Code);
public record TwoFactorVerifySetupResponse(string Message, int BackupCodesRemaining);

// 2FA Login DTOs
public record TwoFactorLoginRequest([MaxLength(500)] string PartialToken, [MaxLength(20)] string Code);
public record TwoFactorLoginResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, bool BackupCodeUsed);

// 2FA Disable DTOs
public record TwoFactorDisableRequest([MaxLength(128)] string Password);

// OAuth DTOs
public record OAuthProviderInfo(string Name, string DisplayName, bool Enabled);
public record OAuthProvidersResponse(List<OAuthProviderInfo> Providers);
public record OAuthLoginUrlResponse(string LoginUrl, string State);
public record OAuthCallbackRequest([MaxLength(2000)] string Code, [MaxLength(500)] string State, DeviceInfo? DeviceInfo = null);
public record DisconnectOAuthRequest([MaxLength(50)] string Provider, [MaxLength(128)] string Password);

/// <summary>
/// Information about a linked OAuth provider.
/// </summary>
public record LinkedOAuthProviderDto(
    Guid Id,
    string Provider,
    string? Email,
    string? DisplayName,
    string? AvatarUrl,
    DateTime LinkedAt,
    DateTime? LastUsedAt
);

/// <summary>
/// Response containing all OAuth providers linked to a user.
/// </summary>
public record UserOAuthProvidersResponse(
    List<LinkedOAuthProviderDto> LinkedProviders,
    List<string> AvailableProviders,
    bool HasPassword
);

/// <summary>
/// Legacy single-provider response for backward compatibility.
/// </summary>
public record UserOAuthInfoResponse(string? Provider, string? ProviderId, bool HasPassword);

// Session DTOs
public record SessionDto(Guid Id, string? DeviceName, string Platform, string IpAddress, string? Location, DateTime LastActiveAt, DateTime CreatedAt, bool IsCurrent);
public record SessionListResponse(Guid CurrentSessionId, List<SessionDto> Sessions);

// Full User Profile (for /auth/me endpoint)
public record UserProfileDto(
    Guid Id,
    string Email,
    string Name,
    string? Mobile,
    string? AvatarUrl,
    bool EmailVerified,
    bool MobileVerified,
    bool Is2faEnabled,
    string Role,
    UserOrganizationDto? Organization,
    List<UserOrganizationDto> Organizations,
    Dictionary<string, object>? Preferences,
    DateTime CreatedAt,
    DateTime? LastLogin
);

public record UserOrganizationDto(Guid Id, string Name, string Role);

// Legacy DTOs for backward compatibility
public record LoginDto([MaxLength(255)] string Email, [MaxLength(128)] string Password);
public record RegisterDto([MaxLength(255)] string Email, [MaxLength(128)] string Password, [MaxLength(100)] string Name, [MaxLength(20)] string? Mobile);
public record AuthResponse(string AccessToken, string RefreshToken, UserDto User, DateTime ExpiresAt);

// ============================================================================
// User DTOs
// ============================================================================

public record UserDto(
    Guid Id,
    string Email,
    string Name,
    string? Mobile,
    string? AvatarUrl,
    string Role,
    UserOrganizationDto? Organization,
    List<UserOrganizationDto> Organizations
);

public record UpdateUserDto([MaxLength(100)] string? Name, [MaxLength(20)] string? Mobile, [MaxLength(500)] string? AvatarUrl, Dictionary<string, object>? Preferences);

// ============================================================================
// Organization DTOs
// ============================================================================

public record CreateOrganizationRequest(
    [MaxLength(200)] string Name,
    [MaxLength(200)] string? LegalName,
    [MaxLength(15)] string Gstin,
    [MaxLength(100)] string? Industry,
    [MaxLength(50)] string State,
    [MaxLength(100)] string? City,
    [MaxLength(50)] string? AnnualTurnoverRange
);

public record CreateOrganizationResponse(
    Guid Id,
    string Name,
    string? LegalName,
    List<GstinDto> Gstins,
    string? Industry,
    string? State,
    string? City,
    string SubscriptionStatus,
    DateTime? TrialEndsAt,
    int MemberCount,
    string CurrentUserRole,
    DateTime CreatedAt,
    string? AccessToken = null,
    int? ExpiresIn = null
);

public record UpdateOrganizationRequest(
    [MaxLength(200)] string? Name,
    [MaxLength(200)] string? LegalName,
    [MaxLength(200)] string? DisplayName,
    [MaxLength(100)] string? Industry,
    [MaxLength(100)] string? SubIndustry,
    [MaxLength(100)] string? BusinessType,
    [MaxLength(50)] string? AnnualTurnoverRange,
    [MaxLength(50)] string? EmployeeCountRange,
    [MaxLength(255)] string? Email,
    [MaxLength(20)] string? Phone,
    [MaxLength(500)] string? Website,
    AddressDto? Address,
    [MaxLength(10)] string? Pan,
    [MaxLength(10)] string? Tan
);

public record AddressDto(
    [MaxLength(200)] string? Line1,
    [MaxLength(200)] string? Line2,
    [MaxLength(100)] string? City,
    [MaxLength(50)] string? State,
    [MaxLength(10)] string? PinCode,
    [MaxLength(50)] string? Country
);

public record OrganizationDetailResponse(
    Guid Id,
    string Name,
    string? LegalName,
    string? DisplayName,
    string? Industry,
    string? SubIndustry,
    string? BusinessType,
    string? AnnualTurnoverRange,
    string? EmployeeCountRange,
    string? Email,
    string? Phone,
    string? Website,
    AddressDto? Address,
    string? Pan,
    List<GstinDto> Gstins,
    SubscriptionInfoDto? Subscription,
    OrganizationSettingsDto? Settings,
    string? LogoUrl,
    int MemberCount,
    string CurrentUserRole,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record OrganizationListItemDto(
    Guid Id,
    string Name,
    string? LogoUrl,
    string Role,
    bool IsExternal,
    int NoticeCount,
    int PendingNoticeCount,
    int MemberCount,
    int GstinCount,
    string SubscriptionStatus
);

public record OrganizationListResponse(
    List<OrganizationListItemDto> Organizations,
    int Total
);

public record SubscriptionInfoDto(
    string Status,
    PlanInfoDto? Plan,
    DateTime? CurrentPeriodEnd,
    UsageInfoDto? Usage
);

public record PlanInfoDto(
    Guid Id,
    string Name,
    int? NoticeLimit,
    int? UserLimit,
    int? GstinLimit
);

public record UsageInfoDto(
    int NoticesThisMonth,
    int Users,
    int Gstins
);

public record OrganizationSettingsDto(
    List<int>? DefaultReminderDays,
    bool NotificationEmail,
    bool NotificationSms,
    bool AllowCaAccess,
    bool RequireResponseApproval,
    string Timezone,
    string Language,
    string DateFormat
);

public record UpdateOrganizationSettingsRequest(
    List<int>? DefaultReminderDays,
    bool? NotificationEmail,
    bool? NotificationSms,
    bool? AllowCaAccess,
    bool? RequireResponseApproval,
    [MaxLength(50)] string? Timezone,
    [MaxLength(10)] string? Language,
    [MaxLength(20)] string? DateFormat
);

public record DeleteOrganizationRequest(
    [MaxLength(100)] string Confirmation,
    [MaxLength(128)] string Password
);

// ============================================================================
// GSTIN DTOs
// ============================================================================

public record GstinDto(
    Guid Id,
    string Gstin,
    string? TradeName,
    string StateCode,
    string StateName,
    string Status,
    bool IsPrimary,
    bool IsVerified,
    DateTime? VerifiedAt
);

public record AddGstinRequest(
    [MaxLength(15)] string Gstin,
    [MaxLength(200)] string? TradeName,
    bool IsPrimary = false
);

public record GstinValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? Gstin,
    string? StateCode,
    string? StateName,
    string? Pan,
    string? EntityCode
);

// ============================================================================
// Organization Member DTOs
// ============================================================================

public record MemberDto(
    Guid Id,
    MemberUserDto User,
    string Role,
    bool IsExternal,
    string Status,
    DateTime? AccessExpiresAt,
    string? ClientReference,
    DateTime JoinedAt,
    DateTime? LastActiveAt
);

public record MemberUserDto(
    Guid Id,
    string Name,
    string Email,
    string? AvatarUrl
);

public record MemberListResponse(
    List<MemberDto> Members,
    int Total,
    int Page,
    int Limit,
    int TotalPages
);

public record ChangeMemberRoleRequest([MaxLength(50)] string Role);

public record ChangeMemberRoleResponse(
    Guid MemberId,
    string PreviousRole,
    string NewRole
);

// ============================================================================
// Member Suspension DTOs
// ============================================================================

public record SuspendMemberRequest(
    [MaxLength(500)] string Reason,
    DateTime? ExpiresAt = null
);

public record MemberSuspensionResponse(
    Guid MemberId,
    Guid UserId,
    string UserEmail,
    string UserName,
    string Status,
    DateTime? SuspendedAt,
    string? SuspensionReason,
    DateTime? SuspensionExpiresAt,
    Guid? SuspendedById,
    string? SuspendedByName
);

// ============================================================================
// Organization Invitation DTOs
// ============================================================================

public record InviteMemberRequest(
    [MaxLength(255)] string Email,
    [MaxLength(50)] string Role,
    bool IsExternal = false,
    int? AccessDurationDays = null,
    [MaxLength(100)] string? ClientReference = null,
    [MaxLength(1000)] string? Message = null
);

public record InvitationDto(
    Guid Id,
    string Email,
    string Role,
    bool IsExternal,
    string Status,
    InvitedByDto InvitedBy,
    DateTime ExpiresAt,
    DateTime LastSentAt,
    int SendCount,
    DateTime CreatedAt
);

public record InvitedByDto(Guid Id, string Name);

public record InvitationListResponse(
    List<InvitationDto> Invitations,
    int Total
);

public record ResendInvitationResponse(
    string Message,
    int SendCount,
    DateTime ExpiresAt
);

public record AcceptInvitationResponse(
    string Message,
    OrganizationBasicDto Organization
);

public record OrganizationBasicDto(
    Guid Id,
    string Name,
    string Role
);

// ============================================================================
// Ownership Transfer DTOs
// ============================================================================

public record TransferOwnershipRequest(
    Guid NewOwnerId,
    [MaxLength(128)] string Password
);

public record TransferOwnershipResponse(
    string Message,
    MemberUserDto NewOwner,
    string YourNewRole
);

// ============================================================================
// Organization Switch DTOs
// ============================================================================

public record SwitchOrganizationRequest(Guid OrganizationId);

public record SwitchOrganizationResponse(
    string AccessToken,
    string RefreshToken,
    OrganizationBasicDto Organization
);

// Legacy DTOs for backward compatibility
public record CreateOrganizationDto(
    [MaxLength(200)] string Name,
    List<string> Gstins,
    [MaxLength(100)] string? Industry,
    [MaxLength(50)] string? State,
    [MaxLength(100)] string? City,
    [MaxLength(500)] string? Address,
    [MaxLength(10)] string? PinCode,
    decimal? AnnualTurnover,
    [MaxLength(10)] string? Pan
);

public record UpdateOrganizationDto(
    [MaxLength(200)] string? Name,
    List<string>? Gstins,
    [MaxLength(100)] string? Industry,
    [MaxLength(50)] string? State,
    [MaxLength(100)] string? City,
    [MaxLength(500)] string? Address,
    [MaxLength(10)] string? PinCode,
    decimal? AnnualTurnover
);

public record OrganizationDto(
    Guid Id,
    string Name,
    List<string> Gstins,
    string? Industry,
    string? State,
    string SubscriptionStatus,
    string? PlanName,
    int MemberCount
);

public record AddMemberDto([MaxLength(255)] string Email, [MaxLength(50)] string Role);

// ============================================================================
// Notice DTOs
// ============================================================================

public record CreateNoticeDto(
    IFormFile File,
    string? Gstin,
    string? NoticeType,
    DateOnly? IssueDate,
    DateOnly? ResponseDeadline,
    List<string>? Tags
);

public record UpdateNoticeDto(
    Guid? AssignedToId,
    string? Status,
    string? Priority,
    DateOnly? ExtendedDeadline,
    List<string>? Tags
);

/// <summary>
/// Request for creating a notice manually without file upload.
/// </summary>
public record CreateManualNoticeRequest
{
    /// <summary>
    /// GSTIN associated with this notice (required).
    /// </summary>
    [MaxLength(15)]
    public required string Gstin { get; init; }

    /// <summary>
    /// Notice number from the authority.
    /// </summary>
    [MaxLength(100)]
    public string? NoticeNumber { get; init; }

    /// <summary>
    /// Notice type code (e.g., DRC-01, ASMT-10, REG-17).
    /// </summary>
    [MaxLength(50)]
    public string? NoticeType { get; init; }

    /// <summary>
    /// Notice category (assessment, demand, registration, refund, audit).
    /// </summary>
    [MaxLength(50)]
    public string? NoticeCategory { get; init; }

    /// <summary>
    /// Notice sub-category for detailed classification.
    /// </summary>
    [MaxLength(100)]
    public string? NoticeSubCategory { get; init; }

    /// <summary>
    /// Date the notice was issued.
    /// </summary>
    public DateOnly? IssueDate { get; init; }

    /// <summary>
    /// Deadline to respond to the notice.
    /// </summary>
    public DateOnly? ResponseDeadline { get; init; }

    /// <summary>
    /// Tax period start date.
    /// </summary>
    public DateOnly? PeriodFrom { get; init; }

    /// <summary>
    /// Tax period end date.
    /// </summary>
    public DateOnly? PeriodTo { get; init; }

    /// <summary>
    /// Hearing date if applicable.
    /// </summary>
    public DateOnly? HearingDate { get; init; }

    /// <summary>
    /// Tax amount demanded.
    /// </summary>
    public decimal? TaxAmount { get; init; }

    /// <summary>
    /// Penalty amount demanded.
    /// </summary>
    public decimal? PenaltyAmount { get; init; }

    /// <summary>
    /// Interest amount demanded.
    /// </summary>
    public decimal? InterestAmount { get; init; }

    /// <summary>
    /// Issuing authority name.
    /// </summary>
    [MaxLength(200)]
    public string? IssuingAuthority { get; init; }

    /// <summary>
    /// Subject/description of the notice.
    /// </summary>
    [MaxLength(500)]
    public string? Subject { get; init; }

    /// <summary>
    /// Priority level (critical, high, medium, low).
    /// </summary>
    [MaxLength(20)]
    public string? Priority { get; init; }

    /// <summary>
    /// Tags for categorization.
    /// </summary>
    public List<string>? Tags { get; init; }

    /// <summary>
    /// User ID to assign the notice to.
    /// </summary>
    public Guid? AssignedToId { get; init; }
}

public record NoticeDto(
    Guid Id,
    string? NoticeType,
    string? NoticeCategory,
    string? NoticeNumber,
    string? Gstin,
    DateOnly? IssueDate,
    DateOnly? ResponseDeadline,
    int? DaysRemaining,
    decimal? TaxAmount,
    decimal? PenaltyAmount,
    string Status,
    string Priority,
    string ProcessingStatus,
    int? RiskScore,
    string? RiskLevel,
    string? SummaryEn,
    Guid? AssignedToId,
    string? AssignedToName,
    DateTime CreatedAt
);

public record NoticeDetailDto(
    Guid Id,
    string? NoticeType,
    string? NoticeCategory,
    string? NoticeNumber,
    string? Gstin,
    DateOnly? IssueDate,
    DateOnly? ResponseDeadline,
    DateOnly? ExtendedDeadline,
    int? DaysRemaining,
    decimal? TaxAmount,
    decimal? PenaltyAmount,
    decimal? InterestAmount,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? IssuingAuthority,
    string Status,
    string Priority,
    string FileUrl,
    string ProcessingStatus,
    List<string>? Tags,
    NoticeAiReportDto? AiReport,
    Guid? AssignedToId,
    string? AssignedToName,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

// Notice Relationship DTOs
public record NoticeRelationshipDto(
    Guid Id,
    Guid SourceNoticeId,
    Guid TargetNoticeId,
    string RelationshipType,
    string? Note,
    NoticeRelationshipNoticeDto SourceNotice,
    NoticeRelationshipNoticeDto TargetNotice,
    string CreatedByName,
    DateTime CreatedAt
);

public record NoticeRelationshipNoticeDto(
    Guid Id,
    string? NoticeNumber,
    string? NoticeType,
    string? Gstin,
    string Status,
    DateOnly? ResponseDeadline
);

public record CreateNoticeRelationshipRequest(
    Guid TargetNoticeId,
    [MaxLength(50)] string RelationshipType,
    [MaxLength(500)] string? Note
);

public record NoticeRelationshipsResponse(
    List<NoticeRelationshipDto> Outgoing,
    List<NoticeRelationshipDto> Incoming
);

public record NoticeFilterDto(
    string? Status,
    string? Priority,
    string? NoticeType,
    string? Gstin,
    DateOnly? DeadlineFrom,
    DateOnly? DeadlineTo,
    string? Search,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "createdAt",
    bool SortDesc = true
);

// ============================================================================
// AI Report DTOs
// ============================================================================

public record NoticeAiReportDto(
    Guid Id,
    int? RiskScore,
    string? RiskLevel,
    string? SummaryEn,
    string? SummaryHi,
    string? PlainEnglish,
    List<ActionItemDto>? ActionItems,
    List<RequiredDocumentDto>? RequiredDocuments,
    List<LegalReferenceDto>? LegalReferences,
    Dictionary<string, int>? ConfidenceScores,
    string? ModelUsed,
    int? ProcessingTimeMs,
    DateTime CreatedAt
);

public record ActionItemDto(int Priority, string Action, string Description, int? DueInDays, string? AssigneeSuggestion);
public record RequiredDocumentDto(string Document, bool Mandatory);
public record LegalReferenceDto(string Section, string Description);

// ============================================================================
// AI Service DTOs
// ============================================================================

public record AiProcessingResult(
    bool Success,
    string? Error,
    AiReportData? Report
);

public record AiReportData(
    int RiskScore,
    string RiskLevel,
    string SummaryEn,
    string SummaryHi,
    string PlainEnglish,
    NoticeMetadata Metadata,
    List<ActionItemDto> ActionItems,
    List<RequiredDocumentDto> RequiredDocuments,
    List<LegalReferenceDto> LegalReferences,
    Dictionary<string, int> ConfidenceScores
);

public record NoticeMetadata(
    string? NoticeType,
    string? NoticeCategory,
    string? NoticeNumber,
    string? Gstin,
    DateOnly? IssueDate,
    DateOnly? ResponseDeadline,
    decimal? TaxAmount,
    decimal? PenaltyAmount,
    decimal? InterestAmount,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? IssuingAuthority
);

public record SimilarNotice(Guid NoticeId, float SimilarityScore, string? NoticeType, string? Summary);

// Similar Notice DTO with enriched notice details
public record SimilarNoticeDto(
    Guid Id,
    string? NoticeNumber,
    string? NoticeType,
    string Status,
    decimal SimilarityScore,
    string? Summary,
    DateOnly? ResponseDeadline
);

// ============================================================================
// Comment DTOs
// ============================================================================

public record CreateCommentDto([MaxLength(10000)] string Content, Guid? ParentId, bool IsInternal, List<Guid>? Mentions);
public record CommentDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string? UserAvatarUrl,
    string Content,
    bool IsInternal,
    Guid? ParentId,
    List<Guid>? Mentions,
    DateTime CreatedAt,
    List<CommentDto>? Replies
);

// ============================================================================
// Task DTOs - See TaskCollaborationDtos.cs for full task module DTOs
// ============================================================================

// Legacy simple task DTOs removed - use CreateTaskDto, UpdateTaskDto, TaskDto from TaskCollaborationDtos.cs

// ============================================================================
// Response DTOs
// ============================================================================

public record CreateResponseDto([MaxLength(100000)] string? DraftContent);
public record UpdateResponseDto([MaxLength(100000)] string? DraftContent, [MaxLength(100000)] string? FinalContent, [MaxLength(50)] string? Status);
public record ResponseDto(
    Guid Id,
    string? DraftContent,
    string? FinalContent,
    string Status,
    int Version,
    Guid CreatedById,
    string CreatedByName,
    Guid? ApprovedById,
    string? ApprovedByName,
    DateTime? SubmittedAt,
    string? SubmissionReference,
    DateTime CreatedAt,
    List<AttachmentDto>? Attachments
);

public record AttachmentDto(
    Guid Id,
    string FileName,
    string FileUrl,
    int? FileSize,
    string? FileType,
    string? DocumentType,
    string? Description,
    int Version = 1,
    bool IsCurrentVersion = true,
    bool HasPreviousVersions = false,
    DateTime? CreatedAt = null);

public record AttachmentVersionDto
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string FileName { get; init; } = string.Empty;
    public int? FileSize { get; init; }
    public string? FileType { get; init; }
    public string? FileHash { get; init; }
    public string? VersionNote { get; init; }
    public bool IsCurrentVersion { get; init; }
    public Guid UploadedById { get; init; }
    public string? UploadedByName { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record AttachmentVersionHistoryResponse
{
    public Guid AttachmentId { get; init; }
    public Guid CurrentVersionId { get; init; }
    public int TotalVersions { get; init; }
    public List<AttachmentVersionDto> Versions { get; init; } = [];
}

public class UploadNewVersionRequest
{
    public IFormFile? File { get; set; }
    [MaxLength(500)]
    public string? VersionNote { get; set; }
}

// ============================================================================
// Common DTOs
// ============================================================================

public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize, int TotalPages);

// ============================================================================
// API Response Wrappers
// ============================================================================

public record ApiResponse<T>(bool Success, T Data);

public record ApiErrorResponse(
    bool Success,
    string Code,
    string Message,
    Dictionary<string, string[]>? Errors = null
);

// ============================================================================
// Approval Chain DTOs
// ============================================================================

public record CreateApprovalChainRequest
{
    [MaxLength(200)]
    public required string Name { get; init; }
    [MaxLength(1000)]
    public string? Description { get; init; }
    [MaxLength(100)]
    public string? TriggerEvent { get; init; }
    public Dictionary<string, object>? TriggerConditions { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsParallel { get; init; }
    public int? MinApprovalsRequired { get; init; }
    public int? DefaultTimeoutHours { get; init; }
    public List<CreateApprovalStepRequest>? Steps { get; init; }
}

public record CreateApprovalStepRequest
{
    [MaxLength(200)]
    public required string Name { get; init; }
    [MaxLength(50)]
    public string? ApproverType { get; init; }
    public Guid? ApproverId { get; init; }
    [MaxLength(50)]
    public string? ApproverRole { get; init; }
    public bool? IsOptional { get; init; }
    public Dictionary<string, object>? Conditions { get; init; }
    public int? TimeoutHours { get; init; }
    public Guid? EscalationUserId { get; init; }
    public bool? AllowDelegation { get; init; }
    [MaxLength(2000)]
    public string? Instructions { get; init; }
}

public record UpdateApprovalChainRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }
    [MaxLength(1000)]
    public string? Description { get; init; }
    [MaxLength(100)]
    public string? TriggerEvent { get; init; }
    public Dictionary<string, object>? TriggerConditions { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsParallel { get; init; }
    public int? MinApprovalsRequired { get; init; }
    public int? DefaultTimeoutHours { get; init; }
}

public record SubmitApprovalRequest
{
    public required Guid ApprovalChainId { get; init; }
    public required Guid NoticeId { get; init; }
    public Guid? ResponseId { get; init; }
    [MaxLength(2000)]
    public string? Notes { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record ApprovalActionRequest
{
    [MaxLength(2000)]
    public string? Comments { get; init; }
}

public record DelegateApprovalRequest
{
    public required Guid DelegateToUserId { get; init; }
    [MaxLength(500)]
    public string? Reason { get; init; }
}

public record ApprovalChainDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? TriggerEvent { get; init; }
    public bool IsActive { get; init; }
    public bool IsParallel { get; init; }
    public int? MinApprovalsRequired { get; init; }
    public int? DefaultTimeoutHours { get; init; }
    public List<ApprovalStepDto> Steps { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public record ApprovalStepDto
{
    public Guid Id { get; init; }
    public int StepOrder { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ApproverType { get; init; } = string.Empty;
    public Guid? ApproverId { get; init; }
    public string? ApproverName { get; init; }
    public string? ApproverRole { get; init; }
    public bool IsOptional { get; init; }
    public int? TimeoutHours { get; init; }
    public bool AllowDelegation { get; init; }
    public string? Instructions { get; init; }
}

public record ApprovalRequestDto
{
    public Guid Id { get; init; }
    public Guid ApprovalChainId { get; init; }
    public string ApprovalChainName { get; init; } = string.Empty;
    public Guid NoticeId { get; init; }
    public string? NoticeNumber { get; init; }
    public Guid? ResponseId { get; init; }
    public Guid RequestedById { get; init; }
    public string RequestedByName { get; init; } = string.Empty;
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? CurrentStepDeadline { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? RequestNotes { get; init; }
    public List<ApprovalActionDto> Actions { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public record ApprovalActionDto
{
    public Guid Id { get; init; }
    public int StepOrder { get; init; }
    public string StepName { get; init; } = string.Empty;
    public Guid ActorId { get; init; }
    public string ActorName { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
    public string? Comments { get; init; }
    public Guid? DelegatedToId { get; init; }
    public string? DelegatedToName { get; init; }
    public bool IsAutomatic { get; init; }
    public DateTime CreatedAt { get; init; }
}
