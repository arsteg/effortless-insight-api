using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

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
    Task<List<SimilarNotice>> FindSimilarNoticesAsync(Guid noticeId, int limit = 5);
}

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream file, string fileName, string contentType);
    Task<Stream> DownloadAsync(string fileUrl);
    Task DeleteAsync(string fileUrl);
    Task<string> GetPresignedUrlAsync(string fileUrl, TimeSpan expiry);
}

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody);
    Task SendTemplateAsync(string to, string templateId, Dictionary<string, object> data);
    Task SendBulkAsync(List<string> recipients, string subject, string htmlBody);
}

// IAuditService is defined in AuditService.cs

// NoticeService implementation is in Services/Notices/NoticeService.cs

public class OrganizationService : IOrganizationService
{
    public Task<Organization> CreateAsync(CreateOrganizationDto dto, Guid ownerId) => throw new NotImplementedException();
    public Task<Organization?> GetByIdAsync(Guid id) => throw new NotImplementedException();
    public Task<Organization> UpdateAsync(Guid id, UpdateOrganizationDto dto) => throw new NotImplementedException();
    public Task DeleteAsync(Guid id) => throw new NotImplementedException();
    public Task<List<ApplicationUser>> GetMembersAsync(Guid organizationId) => throw new NotImplementedException();
    public Task AddMemberAsync(Guid organizationId, AddMemberDto dto) => throw new NotImplementedException();
    public Task RemoveMemberAsync(Guid organizationId, Guid userId) => throw new NotImplementedException();
}

public class UserService : IUserService
{
    public Task<ApplicationUser?> GetByIdAsync(Guid id) => throw new NotImplementedException();
    public Task<ApplicationUser?> GetByEmailAsync(string email) => throw new NotImplementedException();
    public Task<ApplicationUser> UpdateAsync(Guid id, UpdateUserDto dto) => throw new NotImplementedException();
    public Task<AuthResponse> LoginAsync(LoginDto dto) => throw new NotImplementedException();
    public Task<AuthResponse> RegisterAsync(RegisterDto dto) => throw new NotImplementedException();
    public Task<AuthResponse> RefreshTokenAsync(string refreshToken) => throw new NotImplementedException();
    public Task LogoutAsync(Guid userId) => throw new NotImplementedException();
}

public class AiServiceClient : IAiServiceClient
{
    public Task<AiProcessingResult> ProcessNoticeAsync(Guid noticeId, string fileUrl) => throw new NotImplementedException();
    public Task<string> GenerateResponseDraftAsync(Guid noticeId) => throw new NotImplementedException();
    public Task<List<SimilarNotice>> FindSimilarNoticesAsync(Guid noticeId, int limit = 5) => throw new NotImplementedException();
}

// S3FileStorageService implementation is in Services/Storage/S3FileStorageService.cs

public class SendGridEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string htmlBody) => throw new NotImplementedException();
    public Task SendTemplateAsync(string to, string templateId, Dictionary<string, object> data) => throw new NotImplementedException();
    public Task SendBulkAsync(List<string> recipients, string subject, string htmlBody) => throw new NotImplementedException();
}

// AuditServiceImpl is defined in AuditService.cs
