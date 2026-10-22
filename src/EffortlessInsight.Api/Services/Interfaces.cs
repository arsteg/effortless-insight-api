using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services;

public interface INoticeService
{
    Task<Notice> CreateAsync(CreateNoticeDto dto, Guid userId);
    Task<Notice?> GetByIdAsync(Guid id);
    Task<PagedResult<Notice>> GetByOrganizationAsync(Guid organizationId, NoticeFilterDto filter);
    Task<Notice> UpdateAsync(Guid id, UpdateNoticeDto dto);
    Task DeleteAsync(Guid id);
    Task<NoticeAiReport?> GetReportAsync(Guid noticeId);
    Task TriggerAiProcessingAsync(Guid noticeId);
}

// Legacy interfaces - use IOrganizationManagementService and IAuthService instead
[Obsolete("Use IOrganizationManagementService in Services/Organizations instead")]
public interface IOrganizationService
{
    Task<Organization> CreateAsync(CreateOrganizationDto dto, Guid ownerId);
    Task<Organization?> GetByIdAsync(Guid id);
    Task<Organization> UpdateAsync(Guid id, UpdateOrganizationDto dto);
    Task DeleteAsync(Guid id);
    Task<List<ApplicationUser>> GetMembersAsync(Guid organizationId);
    Task AddMemberAsync(Guid organizationId, AddMemberDto dto);
    Task RemoveMemberAsync(Guid organizationId, Guid userId);
}

[Obsolete("Use IAuthService in Services/Auth instead")]
public interface IUserService
{
    Task<ApplicationUser?> GetByIdAsync(Guid id);
    Task<ApplicationUser?> GetByEmailAsync(string email);
    Task<ApplicationUser> UpdateAsync(Guid id, UpdateUserDto dto);
    Task<AuthResponse> LoginAsync(LoginDto dto);
    Task<AuthResponse> RegisterAsync(RegisterDto dto);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken);
    Task LogoutAsync(Guid userId);
}

public interface IAiServiceClient
{
    Task<AiProcessingResult> ProcessNoticeAsync(Guid noticeId, string fileUrl, Guid organizationId);
    Task<string> GenerateResponseDraftAsync(Guid noticeId, Guid organizationId);
    Task<GenerateResponseResult> GenerateResponseDraftAsync(Guid noticeId, Guid organizationId, GenerateResponseOptions options);
    Task<List<SimilarNotice>> FindSimilarNoticesAsync(Guid noticeId, Guid organizationId, int limit = 5);
    Task<HandoverSummaryResult> GenerateHandoverSummaryAsync(HandoverSummaryRequest request, Guid organizationId);

    // Allegation-wise draft generation
    Task<AllegationExtractionResponse> ExtractAllegationsAsync(Guid noticeId, Guid organizationId, string? noticeText = null);
    Task<AllegationWiseDraftResult> GenerateAllegationWiseDraftAsync(Guid noticeId, Guid organizationId, AllegationWiseDraftOptions options);
    Task<AllegationResponseResult> RegenerateAllegationResponseAsync(Guid noticeId, Guid organizationId, RegenerateAllegationOptions options);
}

/// <summary>
/// Request for generating a handover summary.
/// </summary>
public record HandoverSummaryRequest
{
    public string NoticeId { get; init; } = string.Empty;
    public string? NoticeSummary { get; init; }
    public string? NoticeType { get; init; }
    public string? NoticeCategory { get; init; }
    public string? RiskLevel { get; init; }
    public string? ResponseDeadline { get; init; }
    public List<ActivityHistoryItem>? ActivityHistory { get; init; }
    public string? CurrentStatus { get; init; }
    public string? AssignedTo { get; init; }
    public string? AdditionalContext { get; init; }
}

/// <summary>
/// Activity history item for handover summary.
/// </summary>
public record ActivityHistoryItem
{
    public string Action { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
}

/// <summary>
/// Result of handover summary generation.
/// </summary>
public record HandoverSummaryResult
{
    public bool Success { get; init; }
    public HandoverSummaryData? Summary { get; init; }
    public string? Error { get; init; }
    public int ProcessingTimeMs { get; init; }
}

/// <summary>
/// Generated handover summary data.
/// </summary>
public record HandoverSummaryData
{
    public string Background { get; init; } = string.Empty;
    public List<string> WorkCompleted { get; init; } = [];
    public List<string> PendingItems { get; init; } = [];
    public List<string> NextSteps { get; init; } = [];
    public List<HandoverDeadline> KeyDeadlines { get; init; } = [];
    public List<string> KeyContacts { get; init; } = [];
    public string? RiskSummary { get; init; }
    public DateTime GeneratedAt { get; init; }
}

/// <summary>
/// Key deadline in handover summary.
/// </summary>
public record HandoverDeadline
{
    public string Date { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Options for generating a response draft.
/// </summary>
public record GenerateResponseOptions
{
    public string Tone { get; init; } = "formal";
    public string Language { get; init; } = "en";
    public List<string>? PointsToAddress { get; init; }
    public string? AdditionalInstructions { get; init; }
    public Dictionary<string, object>? Context { get; init; }
}

/// <summary>
/// Result of response generation including metadata.
/// </summary>
public record GenerateResponseResult
{
    public bool Success { get; init; }
    public string? Draft { get; init; }
    public string? Error { get; init; }
    public GenerateResponseMetadata? Metadata { get; init; }
}

/// <summary>
/// Metadata about the response generation.
/// </summary>
public record GenerateResponseMetadata
{
    public string Model { get; init; } = "unknown";
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ProcessingTimeMs { get; init; }
}

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream file, string fileName, string contentType);
    Task<Stream> DownloadAsync(string fileUrl);
    Task DeleteAsync(string fileUrl);
    Task<string> GetPresignedUrlAsync(string fileUrl, TimeSpan expiry);
}

// IEmailService is defined in Services/Email (Amazon SES implementation)

// IAuditService is defined in AuditService.cs

// NoticeService implementation is in Services/Notices/NoticeService.cs

// Note: OrganizationService and UserService stub classes removed.
// Real organization management is in Services/Organizations/OrganizationManagementService.cs
// Real authentication is in Services/Auth/AuthService.cs

// AiServiceClientImpl is defined in Services/AiServiceClient.cs

// S3FileStorageService implementation is in Services/Storage/S3FileStorageService.cs

// AuditServiceImpl is defined in AuditService.cs

// ============================================================================
// Allegation-wise Draft Types
// ============================================================================

/// <summary>
/// Options for generating allegation-wise draft responses.
/// </summary>
public record AllegationWiseDraftOptions
{
    public string? NoticeText { get; init; }
    public string Tone { get; init; } = "formal";
    public string Language { get; init; } = "en";
    public List<AllegationData>? Allegations { get; init; }
    public Dictionary<string, string>? AllegationOverrides { get; init; }
    public bool IncludeCaseLaw { get; init; } = true;
    public Dictionary<string, object>? TaxpayerContext { get; init; }
}

/// <summary>
/// Individual allegation data from extraction or input.
/// </summary>
public record AllegationData
{
    public string Id { get; init; } = string.Empty;
    public int SerialNumber { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? LegalProvision { get; init; }
    public decimal? AmountInvolved { get; init; }
    public string? Period { get; init; }
    public List<string>? EvidenceCited { get; init; }
    public string? PageReference { get; init; }
}

/// <summary>
/// Options for regenerating a single allegation response.
/// </summary>
public record RegenerateAllegationOptions
{
    public required AllegationData Allegation { get; init; }
    public string Tone { get; init; } = "formal";
    public string Language { get; init; } = "en";
    public string? CustomInstructions { get; init; }
    public Dictionary<string, object>? TaxpayerContext { get; init; }
}

/// <summary>
/// Result of allegation extraction.
/// </summary>
public record AllegationExtractionResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<AllegationData> Allegations { get; init; } = [];
    public decimal? TotalAmount { get; init; }
    public GenerateResponseMetadata? Metadata { get; init; }
}

/// <summary>
/// Result of allegation-wise draft generation.
/// </summary>
public record AllegationWiseDraftResult
{
    public string? ErrorCode { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<AllegationData> Allegations { get; init; } = [];
    public List<AllegationResponseData> Responses { get; init; } = [];
    public string? CombinedDraft { get; init; }
    public List<string> MissingInfoSummary { get; init; } = [];
    public GenerateResponseMetadata? Metadata { get; init; }
}

/// <summary>
/// AI-generated response for a single allegation.
/// </summary>
public record AllegationResponseData
{
    public string AllegationId { get; init; } = string.Empty;
    public string ResponseText { get; init; } = string.Empty;
    public List<string> LegalBasis { get; init; } = [];
    public List<SupportingDocumentData> SupportingEvidence { get; init; } = [];
    public List<string> MissingInformation { get; init; } = [];
    public List<CaseLawReferenceData>? CaseLawReferences { get; init; }
    public double Confidence { get; init; }
    public string Status { get; init; } = "needs_review";
}

/// <summary>
/// Supporting document reference.
/// </summary>
public record SupportingDocumentData
{
    public string DocumentName { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string Relevance { get; init; } = string.Empty;
    public bool Available { get; init; } = true;
}

/// <summary>
/// Case law citation reference.
/// </summary>
public record CaseLawReferenceData
{
    public string Citation { get; init; } = string.Empty;
    public string Court { get; init; } = string.Empty;
    public string Principle { get; init; } = string.Empty;
    public string Relevance { get; init; } = string.Empty;
}

/// <summary>
/// Result of regenerating a single allegation response.
/// </summary>
public record AllegationResponseResult
{
    public string? ErrorCode { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public AllegationResponseData? Response { get; init; }
}
