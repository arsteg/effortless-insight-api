using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// A CA-initiated invitation for a Business Owner to claim/create an organization
/// for a specific GSTIN. Deliberately distinct from OrganizationInvitation, which
/// invites someone INTO an org that already exists - here the target org does not
/// exist yet, so there is no OrganizationId to point at until acceptance.
///
/// On acceptance (Phase 3), a new Organization is created for the invitee using
/// the GSTIN captured here (not re-entered by the BO), the CA is added to it as a
/// role="ca" OrganizationMember, and any CaStagedNotice rows under the matching
/// CaProspectClient are merged into the new organization's Notices.
/// </summary>
public class CaClientInvitation : BaseEntity
{
    [Required]
    public Guid CaUserId { get; set; }

    [ForeignKey(nameof(CaUserId))]
    public ApplicationUser CaUser { get; set; } = null!;

    /// <summary>
    /// The CA's own firm organization, kept for display/audit on the invitation
    /// (e.g. "Invited by Sharma & Associates").
    /// </summary>
    [Required]
    public Guid CaOrganizationId { get; set; }

    [ForeignKey(nameof(CaOrganizationId))]
    public Organization CaOrganization { get; set; } = null!;

    /// <summary>
    /// GSTIN the BO is being invited to claim. Encrypted at rest (DPDP Act),
    /// same converter as OrganizationGstin.Gstin - never compared with a raw SQL
    /// equality check, always decrypt-and-compare in memory.
    /// </summary>
    [Required]
    [MaxLength(15)]
    public string Gstin { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string EmailNormalized { get; set; } = string.Empty;

    /// <summary>
    /// The CA's own label for this prospective client, shown before the BO's
    /// organization exists (there is no organization name to display yet).
    /// </summary>
    [MaxLength(255)]
    public string? ClientDisplayName { get; set; }

    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Status: pending, accepted, declined, expired, cancelled
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [Required]
    public DateTime ExpiresAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    /// <summary>
    /// The BO user who accepted (once accepted).
    /// </summary>
    public Guid? AcceptedUserId { get; set; }

    [ForeignKey(nameof(AcceptedUserId))]
    public ApplicationUser? AcceptedUser { get; set; }

    /// <summary>
    /// The organization created for the BO on acceptance.
    /// </summary>
    public Guid? ResultingOrganizationId { get; set; }

    [ForeignKey(nameof(ResultingOrganizationId))]
    public Organization? ResultingOrganization { get; set; }

    /// <summary>
    /// Days until access expires after the resulting OrganizationMember is
    /// created (null = permanent), mirrors OrganizationInvitation.AccessDurationDays.
    /// </summary>
    public int? AccessDurationDays { get; set; }

    public DateTime LastSentAt { get; set; } = DateTime.UtcNow;

    public int SendCount { get; set; } = 1;

    [MaxLength(500)]
    public string? Message { get; set; }
}
