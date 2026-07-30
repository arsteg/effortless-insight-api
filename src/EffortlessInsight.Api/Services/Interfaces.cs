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
    Task<AiProcessingResult> ProcessNoticeAsync(Guid noticeId, string fileUrl);
    Task<string> GenerateResponseDraftAsync(Guid noticeId);
    Task<GenerateResponseResult> GenerateResponseDraftAsync(Guid noticeId, GenerateResponseOptions options);
    Task<List<SimilarNotice>> FindSimilarNoticesAsync(Guid noticeId, int limit = 5);
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
