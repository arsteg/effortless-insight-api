namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a refresh token used for JWT token rotation.
/// Implements secure token storage with tracking of usage and revocation.
/// </summary>
public class RefreshToken : BaseEntity
{
    /// <summary>
    /// The unique identifier for this refresh token (JTI claim).
    /// </summary>
    public string Jti { get; set; } = string.Empty;

    /// <summary>
    /// The hashed token value (never store plain text tokens).
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// The user this refresh token belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Navigation property to the user.
    /// </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// The organization context when this token was issued (optional).
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// Navigation property to the organization.
    /// </summary>
    public Organization? Organization { get; set; }

    /// <summary>
    /// When this refresh token expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether this token was issued with "Remember Me" enabled.
    /// </summary>
    public bool RememberMe { get; set; }

    /// <summary>
    /// When this token was revoked (null if still valid).
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Why this token was revoked.
    /// </summary>
    public string? RevokedReason { get; set; }

    /// <summary>
    /// The token that replaced this one (for token rotation tracking).
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>
    /// IP address from which this token was issued.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent of the client that requested this token.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Device fingerprint for token binding (optional).
    /// </summary>
    public string? DeviceFingerprint { get; set; }

    /// <summary>
    /// The session this token is associated with.
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Navigation property to the session.
    /// </summary>
    public UserSession? Session { get; set; }

    /// <summary>
    /// Whether this token is currently valid (not expired and not revoked).
    /// </summary>
    public bool IsValid => !IsExpired && !IsRevoked;

    /// <summary>
    /// Whether this token has expired.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Whether this token has been revoked.
    /// </summary>
    public bool IsRevoked => RevokedAt.HasValue;
}
