using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Tracks admin impersonation of users for support purposes.
/// Impersonation is read-only by default.
/// </summary>
public class ImpersonationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Admin performing the impersonation
    /// </summary>
    public Guid AdminUserId { get; set; }

    [ForeignKey(nameof(AdminUserId))]
    public AdminUser AdminUser { get; set; } = null!;

    /// <summary>
    /// User being impersonated
    /// </summary>
    public Guid TargetUserId { get; set; }

    [ForeignKey(nameof(TargetUserId))]
    public ApplicationUser TargetUser { get; set; } = null!;

    /// <summary>
    /// Organization context for the impersonation
    /// </summary>
    public Guid? TargetOrganizationId { get; set; }

    [ForeignKey(nameof(TargetOrganizationId))]
    public Organization? TargetOrganization { get; set; }

    /// <summary>
    /// Unique token for this impersonation session
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Hashed token for secure lookup
    /// </summary>
    [MaxLength(255)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Permissions granted during impersonation
    /// Default is read-only
    /// </summary>
    public List<string> Permissions { get; set; } = ["read"];

    /// <summary>
    /// Reason for impersonation
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Support ticket ID if applicable
    /// </summary>
    [MaxLength(100)]
    public string? TicketId { get; set; }

    /// <summary>
    /// Session status: active, ended, expired
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "active";

    /// <summary>
    /// When the session expires automatically
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the session was manually ended
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Number of API requests made during impersonation
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Pages/routes visited during impersonation (for audit)
    /// </summary>
    public List<string> PagesVisited { get; set; } = [];

    /// <summary>
    /// IP address of the admin during impersonation
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Timestamps
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Impersonation session status constants
/// </summary>
public static class ImpersonationStatus
{
    public const string Active = "active";
    public const string Ended = "ended";
    public const string Expired = "expired";
}

/// <summary>
/// Impersonation permission constants
/// </summary>
public static class ImpersonationPermissions
{
    /// <summary>
    /// Read-only access (default)
    /// </summary>
    public const string Read = "read";

    /// <summary>
    /// Write access (rarely granted, requires super_admin)
    /// </summary>
    public const string Write = "write";
}
