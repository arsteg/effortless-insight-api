namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a password reset token for secure password recovery.
/// Implements secure token storage with expiry and usage tracking.
/// </summary>
public class PasswordResetToken : BaseEntity
{
    /// <summary>
    /// The hashed token value (never store plain text tokens).
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The user this password reset token belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Navigation property to the user.
    /// </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Email address associated with this reset request.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// When this password reset token expires.
    /// Default is 1 hour from creation.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When this token was used (null if not yet used).
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// IP address from which this token was requested.
    /// </summary>
    public string? RequestedFromIp { get; set; }

    /// <summary>
    /// IP address from which this token was used (null if not yet used).
    /// </summary>
    public string? UsedFromIp { get; set; }

    /// <summary>
    /// User agent of the client that requested this token.
    /// </summary>
    public string? RequestedUserAgent { get; set; }

    /// <summary>
    /// User agent of the client that used this token.
    /// </summary>
    public string? UsedUserAgent { get; set; }

    /// <summary>
    /// Whether this token is currently valid (not expired and not used).
    /// </summary>
    public bool IsValid => !IsExpired && !IsUsed;

    /// <summary>
    /// Whether this token has expired.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Whether this token has been used.
    /// </summary>
    public bool IsUsed => UsedAt.HasValue;
}
