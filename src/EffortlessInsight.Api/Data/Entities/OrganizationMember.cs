using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a user's membership in an organization with role and access settings.
/// Supports multi-organization membership where a user can belong to multiple organizations.
/// </summary>
public class OrganizationMember : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Role within this organization: owner, admin, manager, member, ca, viewer
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = "member";

    /// <summary>
    /// True for external collaborators like CAs who are not direct employees
    /// </summary>
    public bool IsExternal { get; set; }

    /// <summary>
    /// Optional expiry for time-limited access (typically for CA roles)
    /// </summary>
    public DateTime? AccessExpiresAt { get; set; }

    /// <summary>
    /// CA's internal reference for this client organization
    /// </summary>
    [MaxLength(100)]
    public string? ClientReference { get; set; }

    /// <summary>
    /// Status: active, suspended
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "active";

    public DateTime? SuspendedAt { get; set; }

    public Guid? SuspendedById { get; set; }

    [ForeignKey(nameof(SuspendedById))]
    public ApplicationUser? SuspendedBy { get; set; }

    [MaxLength(255)]
    public string? SuspensionReason { get; set; }

    /// <summary>
    /// When the suspension expires and member is automatically reactivated.
    /// Null means indefinite suspension until manually unsuspended.
    /// </summary>
    public DateTime? SuspensionExpiresAt { get; set; }

    /// <summary>
    /// Computed property: true if member is active and not suspended
    /// </summary>
    [NotMapped]
    public bool IsActive => Status == "active" && DeletedAt == null;

    /// <summary>
    /// Computed property: true if member is currently suspended
    /// </summary>
    [NotMapped]
    public bool IsSuspended => Status == "suspended";

    /// <summary>
    /// Per-member notification preferences stored as JSONB
    /// </summary>
    public Dictionary<string, object>? NotificationPreferences { get; set; }

    /// <summary>
    /// User who invited this member
    /// </summary>
    public Guid? InvitedById { get; set; }

    [ForeignKey(nameof(InvitedById))]
    public ApplicationUser? InvitedBy { get; set; }

    /// <summary>
    /// When the user joined this organization
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp for this membership
    /// </summary>
    public DateTime? LastActiveAt { get; set; }

    /// <summary>
    /// Optional reference to a custom role for fine-grained permissions.
    /// If set, permissions are derived from CustomRole instead of the base Role.
    /// </summary>
    public Guid? CustomRoleId { get; set; }

    [ForeignKey(nameof(CustomRoleId))]
    public CustomRole? CustomRole { get; set; }
}
