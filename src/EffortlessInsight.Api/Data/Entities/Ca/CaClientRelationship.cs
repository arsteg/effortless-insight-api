using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Ca;

/// <summary>
/// Represents the relationship between a CA and a Business Owner.
/// The CA has delegated access to the Business Owner's notices for authorized GSTINs.
/// </summary>
public class CaClientRelationship : BaseEntity
{
    /// <summary>
    /// The CA user who has been granted access.
    /// </summary>
    [Required]
    public Guid CaUserId { get; set; }

    [ForeignKey(nameof(CaUserId))]
    public ApplicationUser CaUser { get; set; } = null!;

    /// <summary>
    /// The Business Owner (client) who owns the organization and notices.
    /// </summary>
    [Required]
    public Guid ClientUserId { get; set; }

    [ForeignKey(nameof(ClientUserId))]
    public ApplicationUser ClientUser { get; set; } = null!;

    /// <summary>
    /// The client's organization. Set when the Business Owner creates/links their org.
    /// Nullable because the relationship can exist before the BO creates their organization.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization? Organization { get; set; }

    /// <summary>
    /// Relationship status: pending_invitation, active, revoked, expired.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = CaClientRelationshipStatus.PendingInvitation;

    /// <summary>
    /// When the invitation was sent (relationship initiated).
    /// </summary>
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the client accepted the invitation.
    /// </summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// When the relationship was revoked (if applicable).
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// User who revoked the relationship (CA or BO).
    /// </summary>
    public Guid? RevokedById { get; set; }

    [ForeignKey(nameof(RevokedById))]
    public ApplicationUser? RevokedBy { get; set; }

    /// <summary>
    /// Reason for revocation.
    /// </summary>
    [MaxLength(500)]
    public string? RevocationReason { get; set; }

    /// <summary>
    /// Optional expiry date for the engagement.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// CA's internal reference for this client (for their records).
    /// </summary>
    [MaxLength(100)]
    public string? ClientReference { get; set; }

    /// <summary>
    /// Notes about this client relationship.
    /// </summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Navigation properties
    public ICollection<CaGstinAuthorization> GstinAuthorizations { get; set; } = [];
}

/// <summary>
/// Status constants for CA-Client relationships.
/// </summary>
public static class CaClientRelationshipStatus
{
    /// <summary>
    /// Invitation sent but not yet accepted (BO may not have org yet).
    /// </summary>
    public const string PendingInvitation = "pending_invitation";

    /// <summary>
    /// Relationship is active - CA can access authorized GSTINs.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// Relationship was revoked by CA or Business Owner.
    /// </summary>
    public const string Revoked = "revoked";

    /// <summary>
    /// Relationship expired (engagement end date passed).
    /// </summary>
    public const string Expired = "expired";

    public static readonly string[] All =
    [
        PendingInvitation, Active, Revoked, Expired
    ];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool IsActive(string status) => status == Active;
}
