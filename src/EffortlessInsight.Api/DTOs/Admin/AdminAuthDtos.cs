using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.DTOs.Admin;

// ============================================================================
// Request DTOs
// ============================================================================

public record AdminLoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;
}

public record AdminMfaVerifyRequest
{
    [Required]
    public string SessionToken { get; init; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

public record AdminMfaSetupRequest
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

public record AdminMfaDisableRequest
{
    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

public record AdminChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required]
    [MinLength(16, ErrorMessage = "Admin passwords must be at least 16 characters")]
    public string NewPassword { get; init; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = string.Empty;
}

public record AdminRefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

public record AdminCreateRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [MinLength(16)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = "support_admin";

    public List<string>? Permissions { get; init; }

    public List<string>? IpWhitelist { get; init; }
}

public record AdminUpdateRequest
{
    [MaxLength(100)]
    public string? Name { get; init; }

    public string? Role { get; init; }

    public List<string>? Permissions { get; init; }

    public List<string>? IpWhitelist { get; init; }

    public bool? IsActive { get; init; }
}

// ============================================================================
// Response DTOs
// ============================================================================

public record AdminLoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; init; }
    public DateTime RefreshTokenExpiresAt { get; init; }
    public AdminUserDto User { get; init; } = null!;
}

public record AdminMfaRequiredResponse
{
    public bool MfaRequired { get; init; } = true;
    public string SessionToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}

public record AdminMfaSetupResponse
{
    public string Secret { get; init; } = string.Empty;
    public string QrCodeUri { get; init; } = string.Empty;
    public List<string> BackupCodes { get; init; } = [];
}

public record AdminUserDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public string Role { get; init; } = string.Empty;
    public List<string> Permissions { get; init; } = [];
    public bool MfaEnabled { get; init; }
    public bool IsActive { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record AdminUserDetailDto : AdminUserDto
{
    public List<string>? IpWhitelist { get; init; }
    public string? LastLoginIp { get; init; }
    public DateTime? PasswordChangedAt { get; init; }
    public bool MustChangePassword { get; init; }
    public int FailedLoginAttempts { get; init; }
    public bool IsLocked { get; init; }
    public DateTime? LockedUntil { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public List<AdminAuditLogDto>? RecentActivity { get; init; }
    public List<AdminSessionSummaryDto>? ActiveSessions { get; init; }
}

public record AdminAuditLogDto
{
    public Guid Id { get; init; }
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string? TargetId { get; init; }
    public string? Description { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record AdminSessionSummaryDto
{
    public string SessionId { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
}

public record AdminTokenResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; init; }
    public DateTime RefreshTokenExpiresAt { get; init; }
}

public record AdminSessionDto
{
    public string SessionId { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string? UserAgent { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public bool IsCurrent { get; init; }
}

// ============================================================================
// Validation Result DTOs
// ============================================================================

public record AdminPermissionCheckResult
{
    public bool HasPermission { get; init; }
    public string? MissingPermission { get; init; }
}

public record AdminAuthResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public AdminUserDto? User { get; init; }
    public AdminLoginResponse? LoginResponse { get; init; }
    public AdminMfaRequiredResponse? MfaResponse { get; init; }
}

// ============================================================================
// Error Codes
// ============================================================================

public static class AdminAuthErrors
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string InvalidMfaCode = "INVALID_MFA_CODE";
    public const string MfaSessionExpired = "MFA_SESSION_EXPIRED";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string RefreshTokenExpired = "REFRESH_TOKEN_EXPIRED";
    public const string IpNotWhitelisted = "IP_NOT_WHITELISTED";
    public const string PasswordExpired = "PASSWORD_EXPIRED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string WeakPassword = "WEAK_PASSWORD";
    public const string PasswordReused = "PASSWORD_REUSED";
    public const string SessionNotFound = "SESSION_NOT_FOUND";
}
