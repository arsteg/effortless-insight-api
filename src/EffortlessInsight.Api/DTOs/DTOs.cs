namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// Auth DTOs
// ============================================================================

public record RegisterRequest(
    string Email,
    string Password,
    string Name,
    string? Mobile,
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
    string Email,
    string Password,
    bool RememberMe = false,
    DeviceInfo? DeviceInfo = null
);

public record DeviceInfo(
    string? DeviceId,
    string? DeviceName,
    string Platform // web, ios, android
);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    UserDto User
);

public record TwoFactorRequiredResponse(
    bool Requires2fa,
    string PartialToken,
    int ExpiresIn,
    List<string> Methods
);

public record VerifyEmailRequest(string Token);

public record RefreshTokenRequest(string RefreshToken);

public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn
);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(
    string Token,
    string Password,
    string ConfirmPassword
);

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword
);

public record LogoutRequest(bool AllDevices = false);

// OTP DTOs
public record OtpRequestRequest(string Mobile, string Purpose = "login");
public record OtpResponse(string Message, string MaskedMobile, int ExpiresIn, int RetryAfter);
public record OtpVerifyRequest(string Mobile, string Otp);

// 2FA Setup DTOs
public record TwoFactorSetupResponse(string Secret, string QrCodeDataUrl, string OtpauthUrl, List<string> BackupCodes);
public record TwoFactorVerifySetupRequest(string Code);
public record TwoFactorVerifySetupResponse(string Message, int BackupCodesRemaining);

// 2FA Login DTOs
public record TwoFactorLoginRequest(string PartialToken, string Code);
public record TwoFactorLoginResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, bool BackupCodeUsed);

// 2FA Disable DTOs
public record TwoFactorDisableRequest(string Password);

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
public record LoginDto(string Email, string Password);
public record RegisterDto(string Email, string Password, string Name, string? Mobile);
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
    Guid? OrganizationId,
    string? OrganizationName
);

public record UpdateUserDto(string? Name, string? Mobile, string? AvatarUrl, Dictionary<string, object>? Preferences);

// ============================================================================
// Organization DTOs
// ============================================================================

public record CreateOrganizationDto(
    string Name,
    List<string> Gstins,
    string? Industry,
    string? State,
    string? City,
    string? Address,
    string? PinCode,
    decimal? AnnualTurnover,
    string? Pan
);

public record UpdateOrganizationDto(
    string? Name,
    List<string>? Gstins,
    string? Industry,
    string? State,
    string? City,
    string? Address,
    string? PinCode,
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

public record AddMemberDto(string Email, string Role);

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

// ============================================================================
// Comment DTOs
// ============================================================================

public record CreateCommentDto(string Content, Guid? ParentId, bool IsInternal, List<Guid>? Mentions);
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
// Task DTOs
// ============================================================================

public record CreateTaskDto(string Title, string? Description, Guid? AssignedToId, DateTime? DueDate, string? Priority);
public record UpdateTaskDto(string? Title, string? Description, Guid? AssignedToId, DateTime? DueDate, string? Priority, string? Status);
public record TaskDto(
    Guid Id,
    string Title,
    string? Description,
    Guid? AssignedToId,
    string? AssignedToName,
    DateTime? DueDate,
    string Priority,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

// ============================================================================
// Response DTOs
// ============================================================================

public record CreateResponseDto(string? DraftContent);
public record UpdateResponseDto(string? DraftContent, string? FinalContent, string? Status);
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

public record AttachmentDto(Guid Id, string FileName, string FileUrl, int? FileSize, string? FileType, string? DocumentType);

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
