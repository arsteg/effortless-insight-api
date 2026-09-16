using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using EffortlessInsight.Api.Data.Entities.Ca;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a GSTIN (Goods and Services Tax Identification Number) registered under an organization.
/// Each organization can have multiple GSTINs across different states.
/// </summary>
public class OrganizationGstin : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// 15-character GSTIN in format: 2-digit state + 10-char PAN + 1 entity + 1Z + 1 check
    /// Note: MaxLength is 255 to accommodate encrypted storage (DPDP Act compliance)
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Gstin { get; set; } = string.Empty;

    /// <summary>
    /// Business trade name for this GSTIN registration
    /// </summary>
    [MaxLength(255)]
    public string? TradeName { get; set; }

    /// <summary>
    /// Legal name for this GSTIN registration
    /// </summary>
    [MaxLength(255)]
    public string? LegalName { get; set; }

    /// <summary>
    /// First 2 digits of GSTIN representing the state code
    /// </summary>
    [Required]
    [MaxLength(2)]
    public string StateCode { get; set; } = string.Empty;

    /// <summary>
    /// State name derived from state code
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string StateName { get; set; } = string.Empty;

    /// <summary>
    /// Address line 1 for this specific registration
    /// </summary>
    [MaxLength(255)]
    public string? AddressLine1 { get; set; }

    /// <summary>
    /// Address line 2 for this specific registration
    /// </summary>
    [MaxLength(255)]
    public string? AddressLine2 { get; set; }

    /// <summary>
    /// City for this registration
    /// </summary>
    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// PIN code for this registration
    /// </summary>
    [MaxLength(10)]
    public string? PinCode { get; set; }

    /// <summary>
    /// Date when this GSTIN was registered
    /// </summary>
    public DateOnly? RegistrationDate { get; set; }

    /// <summary>
    /// Date when this GSTIN was cancelled (if applicable)
    /// </summary>
    public DateOnly? CancellationDate { get; set; }

    /// <summary>
    /// Status: active, suspended, cancelled
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "active";

    /// <summary>
    /// Whether this GSTIN has been verified against GST portal
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// When verification was performed
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Source of verification: manual, gst_portal, api
    /// </summary>
    [MaxLength(50)]
    public string? VerificationSource { get; set; }

    /// <summary>
    /// Whether this is the primary GSTIN for the organization
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// How this GSTIN entered the registry — distinguishes the organization's
    /// own registrations from client GSTINs auto-registered by GST notice sync.
    /// See <see cref="OrganizationGstinSource"/>.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = OrganizationGstinSource.Manual;

    // ============================================================================
    // GSTIN Claiming (CA Distribution Channel)
    // ============================================================================

    /// <summary>
    /// Whether this GSTIN has been claimed by a Business Owner.
    /// A GSTIN can only be claimed once across all organizations.
    /// </summary>
    public bool IsClaimed { get; set; }

    /// <summary>
    /// Business Owner who claimed this GSTIN.
    /// </summary>
    public Guid? ClaimedByUserId { get; set; }

    [ForeignKey(nameof(ClaimedByUserId))]
    public ApplicationUser? ClaimedByUser { get; set; }

    /// <summary>
    /// When this GSTIN was claimed.
    /// </summary>
    public DateTime? ClaimedAt { get; set; }

    // ============================================================================
    // GSTN Portal Integration
    // ============================================================================

    /// <summary>
    /// When notices were last synced from the GST portal for this GSTIN.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Navigation property to the GSTN portal connection.
    /// </summary>
    public GstnConnection? GstnConnection { get; set; }

    // ============================================================================
    // CA Navigation Properties
    // ============================================================================

    /// <summary>
    /// CA authorizations for this GSTIN.
    /// </summary>
    public ICollection<CaGstinAuthorization> CaAuthorizations { get; set; } = [];
}

/// <summary>
/// How an OrganizationGstin entry was created.
/// </summary>
public static class OrganizationGstinSource
{
    /// <summary>The organization's own GSTIN, registered during org creation.</summary>
    public const string Onboarding = "onboarding";

    /// <summary>Added by a user via Settings → Add GSTIN.</summary>
    public const string Manual = "manual";

    /// <summary>A client GSTIN auto-registered by GST notice sync (unverified).</summary>
    public const string GstSync = "gst_sync";

    /// <summary>GSTIN claimed via CA invitation acceptance.</summary>
    public const string CaInvitation = "ca_invitation";

    public static readonly string[] All = [Onboarding, Manual, GstSync, CaInvitation];

    public static bool IsValid(string source) => All.Contains(source);
}
