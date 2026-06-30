using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Audit log for all authentication-related events.
/// </summary>
public class LoginAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [MaxLength(255)]
    public string? EmailAttempted { get; set; }

    /// <summary>
    /// Type of authentication event.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string EventType { get; set; } = AuthEventTypes.Login;

    public bool Success { get; set; }

    [MaxLength(100)]
    public string? FailureReason { get; set; }

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    public string? UserAgent { get; set; }

    [MaxLength(100)]
    public string? LocationCity { get; set; }

    [MaxLength(100)]
    public string? LocationCountry { get; set; }

    [Required]
    [MaxLength(30)]
    public string AuthMethod { get; set; } = "password";

    /// <summary>
    /// Additional context data as JSON (e.g., session ID for revocation events).
    /// </summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Constants for authentication event types.
/// </summary>
public static class AuthEventTypes
{
    // Login events
    public const string Login = "login";
    public const string LoginFailed = "login_failed";
    public const string Logout = "logout";
    public const string Register = "register";

    // Password events
    public const string PasswordChange = "password_change";
    public const string PasswordResetRequest = "password_reset_request";
    public const string PasswordResetComplete = "password_reset_complete";

    // 2FA events
    public const string TwoFactorEnabled = "2fa_enabled";
    public const string TwoFactorDisabled = "2fa_disabled";
    public const string TwoFactorLogin = "2fa_login";
    public const string TwoFactorFailed = "2fa_failed";

    // Session events
    public const string SessionRevoked = "session_revoked";
    public const string AllSessionsRevoked = "all_sessions_revoked";

    // Email events
    public const string EmailVerified = "email_verified";

    // OAuth events
    public const string OAuthLogin = "oauth_login";
    public const string OAuthLinked = "oauth_linked";
    public const string OAuthDisconnected = "oauth_disconnected";

    // Account status events
    public const string AccountLocked = "account_locked";
    public const string AccountUnlocked = "account_unlocked";
}
