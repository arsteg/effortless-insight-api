using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Ca;

/// <summary>
/// Represents a CA's authorization to access a specific GSTIN.
/// The CA can only access notices for GSTINs they are explicitly authorized for.
/// </summary>
public class CaGstinAuthorization : BaseEntity
{
    /// <summary>
    /// The CA-Client relationship this authorization belongs to.
    /// </summary>
    [Required]
    public Guid CaClientRelationshipId { get; set; }

    [ForeignKey(nameof(CaClientRelationshipId))]
    public CaClientRelationship CaClientRelationship { get; set; } = null!;

    /// <summary>
    /// The GSTIN being authorized. Stored as plain GSTIN string.
    /// </summary>
    [Required]
    [MaxLength(15)]
    public string Gstin { get; set; } = string.Empty;

    /// <summary>
    /// Link to the OrganizationGstin record (set when GSTIN is claimed by BO).
    /// Nullable because authorization can exist before BO claims the GSTIN.
    /// </summary>
    public Guid? OrganizationGstinId { get; set; }

    [ForeignKey(nameof(OrganizationGstinId))]
    public OrganizationGstin? OrganizationGstin { get; set; }

    /// <summary>
    /// Authorization status: pending_claim, active, revoked.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = CaGstinAuthorizationStatus.PendingClaim;

    /// <summary>
    /// Permissions granted to the CA for this GSTIN.
    /// Stored as JSON array: ["sync", "view", "comment", "draft_response"]
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<string> Permissions { get; set; } = [CaPermission.Sync, CaPermission.View];

    /// <summary>
    /// When this authorization was granted.
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this authorization was revoked (if applicable).
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// User who revoked this authorization.
    /// </summary>
    public Guid? RevokedById { get; set; }

    [ForeignKey(nameof(RevokedById))]
    public ApplicationUser? RevokedBy { get; set; }

    /// <summary>
    /// Reason for revocation.
    /// </summary>
    [MaxLength(500)]
    public string? RevocationReason { get; set; }
}

/// <summary>
/// Status constants for GSTIN authorizations.
/// </summary>
public static class CaGstinAuthorizationStatus
{
    /// <summary>
    /// GSTIN has not been claimed by Business Owner yet.
    /// CA can stage notices but they won't be linked to an organization.
    /// </summary>
    public const string PendingClaim = "pending_claim";

    /// <summary>
    /// Authorization is active - CA can access notices for this GSTIN.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// Authorization was revoked by Business Owner or CA.
    /// </summary>
    public const string Revoked = "revoked";

    public static readonly string[] All = [PendingClaim, Active, Revoked];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool CanAccess(string status) => status == Active;
}

/// <summary>
/// Permission constants for CA GSTIN authorizations.
/// </summary>
public static class CaPermission
{
    /// <summary>
    /// Can sync notices from GST portal for this GSTIN.
    /// </summary>
    public const string Sync = "sync";

    /// <summary>
    /// Can view notices for this GSTIN.
    /// </summary>
    public const string View = "view";

    /// <summary>
    /// Can add comments to notices for this GSTIN.
    /// </summary>
    public const string Comment = "comment";

    /// <summary>
    /// Can draft responses for notices (but not send).
    /// </summary>
    public const string DraftResponse = "draft_response";

    public static readonly string[] All = [Sync, View, Comment, DraftResponse];

    /// <summary>
    /// Default permissions granted to CA when authorization is created.
    /// </summary>
    public static readonly string[] DefaultPermissions = [Sync, View];

    public static bool IsValid(string permission) => All.Contains(permission);
}
