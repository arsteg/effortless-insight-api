using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using EffortlessInsight.Api.Data.Entities.Admin;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// An admin-granted, audited "Free CA Access" record for a self-registered CA
/// (ApplicationUser.IsCA == true). Modeled per-CA-user, not per-organization or
/// globally: FeatureAccessService checks for an active row here for the org's
/// owning user, in addition to (not instead of) the existing
/// SubscriptionPlan.IsCaOperatorPlan bypass.
///
/// Revoking never deletes or overwrites a row - it flips IsActive to false and a
/// later re-grant inserts a fresh row, so the full grant/revoke history for a CA
/// is preserved for audit purposes.
/// </summary>
public class CaFreeAccessGrant : BaseEntity
{
    [Required]
    public Guid CaUserId { get; set; }

    [ForeignKey(nameof(CaUserId))]
    public ApplicationUser CaUser { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    [Required]
    public Guid GrantedByAdminId { get; set; }

    [ForeignKey(nameof(GrantedByAdminId))]
    public AdminUser GrantedByAdmin { get; set; } = null!;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(500)]
    public string GrantReason { get; set; } = string.Empty;

    public Guid? RevokedByAdminId { get; set; }

    [ForeignKey(nameof(RevokedByAdminId))]
    public AdminUser? RevokedByAdmin { get; set; }

    public DateTime? RevokedAt { get; set; }

    [MaxLength(500)]
    public string? RevokeReason { get; set; }
}
