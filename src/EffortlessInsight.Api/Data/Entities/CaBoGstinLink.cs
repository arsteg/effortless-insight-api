using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks which GSTINs connect a CA's organization to a Business Owner's organization.
/// This enables cross-organization notice visibility based on shared GSTINs.
/// </summary>
/// <remarks>
/// When a BO accepts a CA invitation for a specific GSTIN, a CaBoGstinLink is created.
/// This link enables:
/// - CA to see BO's notices for that GSTIN
/// - BO to see CA's notices for that GSTIN (if CA has uploaded any)
///
/// Links are created/updated when:
/// - BO accepts CA invitation (AcceptInvitationAsync)
/// - BO links existing organization to CA (AcceptInvitationLinkAsync)
/// </remarks>
public class CaBoGstinLink : BaseEntity
{
    /// <summary>
    /// The CA's organization ID (where the CA is owner).
    /// </summary>
    [Required]
    public Guid CaOrganizationId { get; set; }

    [ForeignKey(nameof(CaOrganizationId))]
    public Organization CaOrganization { get; set; } = null!;

    /// <summary>
    /// The Business Owner's organization ID.
    /// </summary>
    [Required]
    public Guid BoOrganizationId { get; set; }

    [ForeignKey(nameof(BoOrganizationId))]
    public Organization BoOrganization { get; set; } = null!;

    /// <summary>
    /// SHA-256 hash of the normalized (uppercase) GSTIN.
    /// Used for efficient joining with Notice.GstinHash.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string GstinHash { get; set; } = string.Empty;

    /// <summary>
    /// The CA user who established this connection.
    /// </summary>
    [Required]
    public Guid CaUserId { get; set; }

    [ForeignKey(nameof(CaUserId))]
    public ApplicationUser CaUser { get; set; } = null!;

    /// <summary>
    /// Reference to the CA's membership in the BO's organization.
    /// This membership grants the CA access to the BO's notices for this GSTIN.
    /// </summary>
    [Required]
    public Guid CaMembershipId { get; set; }

    [ForeignKey(nameof(CaMembershipId))]
    public OrganizationMember CaMembership { get; set; } = null!;

    /// <summary>
    /// Whether this link is currently active.
    /// Deactivated when CA membership is revoked or expires.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When the link was deactivated (if applicable).
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// Reason for deactivation.
    /// </summary>
    [MaxLength(255)]
    public string? DeactivationReason { get; set; }
}
