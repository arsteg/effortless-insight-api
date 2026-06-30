using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a linked OAuth provider for a user.
/// Supports multiple OAuth providers per user (e.g., both Google and Microsoft).
/// </summary>
public class UserOAuthProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The user this OAuth provider is linked to.
    /// </summary>
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// OAuth provider name (google, microsoft, etc.).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// User's unique ID from the OAuth provider.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Email address from the OAuth provider (may differ from user's primary email).
    /// </summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Display name from the OAuth provider.
    /// </summary>
    [MaxLength(255)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Avatar/profile picture URL from the OAuth provider.
    /// </summary>
    [MaxLength(1000)]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// OAuth access token (encrypted). Used for making API calls on user's behalf.
    /// </summary>
    public string? AccessTokenEncrypted { get; set; }

    /// <summary>
    /// OAuth refresh token (encrypted). Used to obtain new access tokens.
    /// </summary>
    public string? RefreshTokenEncrypted { get; set; }

    /// <summary>
    /// When the access token expires.
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// OAuth scopes granted by the user.
    /// </summary>
    [MaxLength(1000)]
    public string? Scopes { get; set; }

    /// <summary>
    /// When this OAuth provider was first linked.
    /// </summary>
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the user last authenticated via this provider.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Metadata from the OAuth provider (JSON).
    /// </summary>
    public string? Metadata { get; set; }
}
