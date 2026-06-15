using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Represents an admin user session for security tracking and session management.
/// </summary>
public class AdminSession
{
    /// <summary>
    /// Unique session identifier (GUID string for JWT claims).
    /// </summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Reference to the admin user.
    /// </summary>
    [Required]
    public Guid AdminUserId { get; set; }

    /// <summary>
    /// Refresh token for this session.
    /// </summary>
    [MaxLength(128)]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Refresh token expiry time.
    /// </summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }

    /// <summary>
    /// Client IP address at session creation.
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Client user agent string.
    /// </summary>
    [MaxLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Device fingerprint for additional security.
    /// </summary>
    [MaxLength(256)]
    public string? DeviceFingerprint { get; set; }

    /// <summary>
    /// Approximate location derived from IP (city, country).
    /// </summary>
    [MaxLength(100)]
    public string? Location { get; set; }

    /// <summary>
    /// Session creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp for idle timeout.
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Session expiration timestamp.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the session is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When the session was invalidated (logout or force-expire).
    /// </summary>
    public DateTime? InvalidatedAt { get; set; }

    /// <summary>
    /// Reason for invalidation.
    /// </summary>
    [MaxLength(100)]
    public string? InvalidationReason { get; set; }

    /// <summary>
    /// Navigation property to admin user.
    /// </summary>
    public AdminUser? AdminUser { get; set; }
}

/// <summary>
/// Common invalidation reasons for admin sessions.
/// </summary>
public static class SessionInvalidationReasons
{
    public const string Logout = "logout";
    public const string Expired = "expired";
    public const string IdleTimeout = "idle_timeout";
    public const string ForceLogout = "force_logout";
    public const string PasswordChange = "password_change";
    public const string MfaDisabled = "mfa_disabled";
    public const string AccountSuspended = "account_suspended";
    public const string SecurityConcern = "security_concern";
    public const string SessionLimitExceeded = "session_limit_exceeded";
}
