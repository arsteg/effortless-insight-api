using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a pending invitation to join an organization.
/// Invitations have expiry times and track send attempts.
/// </summary>
public class OrganizationInvitation : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// User who sent the invitation
    /// </summary>
    [Required]
    public Guid InvitedById { get; set; }

    [ForeignKey(nameof(InvitedById))]
    public ApplicationUser InvitedBy { get; set; } = null!;

    /// <summary>
    /// Email address of the invitee
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Normalized (lowercase, trimmed) email for uniqueness checks
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string EmailNormalized { get; set; } = string.Empty;

    /// <summary>
    /// Role to be assigned upon acceptance: admin, manager, member, ca, viewer
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = "member";

    /// <summary>
    /// Whether this is an external collaborator invitation (e.g., CA)
    /// </summary>
    public bool IsExternal { get; set; }

    /// <summary>
    /// Number of days until access expires after joining (null for permanent)
    /// </summary>
    public int? AccessDurationDays { get; set; }

    /// <summary>
    /// SHA-256 hash of the invitation token
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Status: pending, accepted, declined, expired, cancelled
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// When this invitation expires
    /// </summary>
    [Required]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the invitee responded to the invitation
    /// </summary>
    public DateTime? RespondedAt { get; set; }

    /// <summary>
    /// User who accepted the invitation (once accepted)
    /// </summary>
    public Guid? AcceptedUserId { get; set; }

    [ForeignKey(nameof(AcceptedUserId))]
    public ApplicationUser? AcceptedUser { get; set; }

    /// <summary>
    /// When the invitation email was last sent
    /// </summary>
    public DateTime LastSentAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of times the invitation email has been sent
    /// </summary>
    public int SendCount { get; set; } = 1;

    /// <summary>
    /// Personal message included in the invitation email
    /// </summary>
    [MaxLength(500)]
    public string? Message { get; set; }
}
