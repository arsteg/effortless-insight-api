using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// A "virtual client" a CA is working with before the Business Owner has
/// accepted an invitation and claimed a real Organization. Keyed by
/// (CaUserId, Gstin), never by OrganizationId - this is exactly the staging
/// area the CA-as-distributor flow needs so a CA can sync/upload notices for
/// a client's GSTIN before that client has an account.
///
/// Deliberately unrelated to GstSync's GstClient entity, which is a portal-sync
/// automation record scoped to an existing Organization - do not conflate the two.
/// </summary>
public class CaProspectClient : BaseEntity
{
    [Required]
    public Guid CaUserId { get; set; }

    [ForeignKey(nameof(CaUserId))]
    public ApplicationUser CaUser { get; set; } = null!;

    /// <summary>
    /// Encrypted at rest (DPDP Act), same converter as OrganizationGstin.Gstin.
    /// </summary>
    [Required]
    [MaxLength(15)]
    public string Gstin { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex hash of the normalized GSTIN. Exists purely so a database
    /// unique index can enforce one prospect client per (CaUserId, GSTIN) even
    /// though the Gstin column itself is non-deterministic ciphertext.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string GstinHash { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? ClientDisplayName { get; set; }

    /// <summary>
    /// Nullable: a CA can start staging/syncing a client's notices before ever
    /// sending an invitation.
    /// </summary>
    public Guid? CaClientInvitationId { get; set; }

    [ForeignKey(nameof(CaClientInvitationId))]
    public CaClientInvitation? CaClientInvitation { get; set; }

    /// <summary>
    /// Status: staging (pre-acceptance, CA actively working it), merged (handoff
    /// complete, data moved into a real Organization), superseded (GSTIN was
    /// re-associated with a different CA before this one's invitation was accepted).
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "staging";

    public DateTime? MergedAt { get; set; }

    public Guid? MergedIntoOrganizationId { get; set; }

    [ForeignKey(nameof(MergedIntoOrganizationId))]
    public Organization? MergedIntoOrganization { get; set; }

    public ICollection<CaStagedNotice> StagedNotices { get; set; } = [];
}
