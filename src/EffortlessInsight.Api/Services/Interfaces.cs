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

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody);
    Task SendTemplateAsync(string to, string templateId, Dictionary<string, object> data);
    Task SendBulkAsync(List<string> recipients, string subject, string htmlBody);
}

// IAuditService is defined in AuditService.cs

// NoticeService implementation is in Services/Notices/NoticeService.cs

// Note: OrganizationService and UserService stub classes removed.
// Real organization management is in Services/Organizations/OrganizationManagementService.cs
// Real authentication is in Services/Auth/AuthService.cs

// AiServiceClientImpl is defined in Services/AiServiceClient.cs

// S3FileStorageService implementation is in Services/Storage/S3FileStorageService.cs

public class ResendEmailServiceImpl : IEmailService
{
    private readonly Resend.IResend _resend;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResendEmailServiceImpl> _logger;

    public ResendEmailServiceImpl(Resend.IResend resend, IConfiguration configuration, ILogger<ResendEmailServiceImpl> logger)
    {
        _resend = resend;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        try
        {
            var fromEmail = _configuration["Resend:FromEmail"] ?? "onboarding@resend.dev";
            var fromName = _configuration["Resend:FromName"] ?? "EffortlessInsight";

            var message = new Resend.EmailMessage
            {
                From = $"{fromName} <{fromEmail}>",
                To = [to],
                Subject = subject,
                HtmlBody = htmlBody
            };

            var response = await _resend.EmailSendAsync(message);
            if (!response.Success)
            {
                _logger.LogError("Failed to send email to {To}: {Error}", to, response.Exception?.Message);
                return; // Don't throw, just log the error
            }

            _logger.LogInformation("Email sent successfully to {To}, MessageId: {MessageId}", to, response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending email to {To}", to);
            // Don't rethrow - let the calling code continue
        }
    }

    public async Task SendTemplateAsync(string to, string templateId, Dictionary<string, object> data)
    {
        // Render template based on templateId
        var (subject, htmlBody) = RenderTemplate(templateId, data);
        await SendAsync(to, subject, htmlBody);
    }

    public async Task SendBulkAsync(List<string> recipients, string subject, string htmlBody)
    {
        foreach (var recipient in recipients)
        {
            try
            {
                await SendAsync(recipient, subject, htmlBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send bulk email to {Recipient}", recipient);
            }
        }
    }

    private (string Subject, string HtmlBody) RenderTemplate(string templateId, Dictionary<string, object> data)
    {
        var baseUrl = _configuration["App:BaseUrl"] ?? "http://localhost:3000";

        return templateId switch
        {
            "auth_verify_email" => (
                "Verify your email - EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                    <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
                        <h1 style="color: white; margin: 0; font-size: 24px;">EffortlessInsight</h1>
                    </div>
                    <div style="background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;">
                        <h2 style="color: #333; margin-top: 0;">Verify Your Email</h2>
                        <p>Hi {data.GetValueOrDefault("user_name", "there")},</p>
                        <p>Thanks for signing up! Please verify your email address by clicking the button below:</p>
                        <div style="text-align: center; margin: 30px 0;">
                            <a href="{data.GetValueOrDefault("verification_link", "#")}"
                               style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;">
                                Verify Email Address
                            </a>
                        </div>
                        <p style="color: #666; font-size: 14px;">This link will expire in {data.GetValueOrDefault("expiry_hours", "24")} hours.</p>
                        <p style="color: #666; font-size: 14px;">If you didn't create an account, you can safely ignore this email.</p>
                        <hr style="border: none; border-top: 1px solid #e0e0e0; margin: 30px 0;">
                        <p style="color: #999; font-size: 12px; margin: 0;">
                            If the button doesn't work, copy and paste this link into your browser:<br>
                            <a href="{data.GetValueOrDefault("verification_link", "#")}" style="color: #667eea;">{data.GetValueOrDefault("verification_link", "#")}</a>
                        </p>
                    </div>
                </body>
                </html>
                """
            ),
            "auth_password_reset" => (
                "Reset your password - EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                    <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
                        <h1 style="color: white; margin: 0; font-size: 24px;">EffortlessInsight</h1>
                    </div>
                    <div style="background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;">
                        <h2 style="color: #333; margin-top: 0;">Reset Your Password</h2>
                        <p>Hi {data.GetValueOrDefault("user_name", "there")},</p>
                        <p>We received a request to reset your password. Click the button below to create a new password:</p>
                        <div style="text-align: center; margin: 30px 0;">
                            <a href="{data.GetValueOrDefault("reset_link", "#")}"
                               style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;">
                                Reset Password
                            </a>
                        </div>
                        <p style="color: #666; font-size: 14px;">This link will expire in {data.GetValueOrDefault("expiry_hours", "24")} hours.</p>
                        <p style="color: #666; font-size: 14px;">If you didn't request a password reset, you can safely ignore this email.</p>
                    </div>
                </body>
                </html>
                """
            ),
            _ => (
                "Notification from EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <body style="font-family: Arial, sans-serif; padding: 20px;">
                    <h2>EffortlessInsight</h2>
                    <p>{data.GetValueOrDefault("message", "You have a new notification.")}</p>
                </body>
                </html>
                """
            )
        };
    }
}

// AuditServiceImpl is defined in AuditService.cs
