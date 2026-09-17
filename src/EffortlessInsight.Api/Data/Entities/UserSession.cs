using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Required]
    [MaxLength(64)]
    public string RefreshTokenHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(36)]
    public string RefreshTokenJti { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? DeviceId { get; set; }

    [MaxLength(255)]
    public string? DeviceName { get; set; }

    [Required]
    [MaxLength(20)]
    public string Platform { get; set; } = "web"; // web, ios, android

    public string? UserAgent { get; set; }

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LocationCity { get; set; }

    [MaxLength(100)]
    public string? LocationCountry { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    [MaxLength(50)]
    public string? RevokedReason { get; set; } // logout, password_change, admin, security

    // ============================================================================
    // Token context
    // ============================================================================

    /// <summary>
    /// The organization this session's tokens were minted for.
    /// Refresh reproduces this instead of guessing from the user's memberships, which is
    /// what used to throw a multi-org user back to an arbitrary organization every 15 minutes.
    /// Null on sessions created before this column existed - those keep the legacy behaviour.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// The role the tokens were minted with, for <see cref="OrganizationId"/>.
    /// </summary>
    [MaxLength(50)]
    public string? Role { get; set; }

    /// <summary>
    /// Whether the tokens were minted for an external collaborator (a CA acting for a client).
    /// </summary>
    public bool IsExternal { get; set; }

    /// <summary>
    /// Set only when a CA is acting on behalf of a client: the CaClientRelationship granting it.
    /// Re-validated on every refresh, and revoked in bulk when the relationship ends.
    /// </summary>
    public Guid? CaClientRelationshipId { get; set; }
}
