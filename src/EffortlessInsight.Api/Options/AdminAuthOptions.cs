using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for admin authentication.
/// </summary>
public class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    /// <summary>
    /// JWT signing secret (minimum 32 characters).
    /// Should be different from the main app JWT secret.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// JWT issuer.
    /// </summary>
    public string JwtIssuer { get; set; } = "EffortlessInsight-Admin";

    /// <summary>
    /// JWT audience.
    /// </summary>
    public string JwtAudience { get; set; } = "EffortlessInsight-Admin-Portal";

    /// <summary>
    /// Access token expiry in minutes.
    /// Default: 30 minutes (shorter than regular users for security).
    /// </summary>
    public int AccessTokenExpiryMinutes { get; set; } = 30;

    /// <summary>
    /// Refresh token expiry in days.
    /// Default: 7 days.
    /// </summary>
    public int RefreshTokenExpiryDays { get; set; } = 7;

    /// <summary>
    /// Maximum failed login attempts before lockout.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Lockout duration in minutes after max failed attempts.
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Session timeout in minutes (idle timeout).
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// MFA issuer name for TOTP apps.
    /// </summary>
    public string MfaIssuer { get; set; } = "EffortlessInsight Admin";

    /// <summary>
    /// Encryption key for MFA secrets (must be 32 bytes base64).
    /// </summary>
    public string MfaEncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// Require MFA for all admin accounts.
    /// </summary>
    public bool RequireMfa { get; set; } = true;

    /// <summary>
    /// Password minimum length for admin accounts.
    /// </summary>
    public int PasswordMinLength { get; set; } = 16;

    /// <summary>
    /// Number of previous passwords to remember to prevent reuse.
    /// </summary>
    public int PasswordHistoryCount { get; set; } = 5;

    /// <summary>
    /// Password expiry in days (0 = no expiry).
    /// </summary>
    public int PasswordExpiryDays { get; set; } = 90;

    /// <summary>
    /// Enable IP whitelisting for admin access.
    /// </summary>
    public bool EnableIpWhitelisting { get; set; } = false;

    /// <summary>
    /// Global IP whitelist (CIDR notation).
    /// Only used if EnableIpWhitelisting is true.
    /// </summary>
    public List<string> GlobalIpWhitelist { get; set; } = [];

    /// <summary>
    /// Send email alerts on new device/location login.
    /// </summary>
    public bool SendLoginAlerts { get; set; } = true;

    /// <summary>
    /// Admin portal base URL for email links (e.g., password reset).
    /// </summary>
    public string AdminPortalUrl { get; set; } = "http://localhost:3001";

    /// <summary>
    /// Maximum concurrent sessions per admin.
    /// 0 = unlimited.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 3;
}
