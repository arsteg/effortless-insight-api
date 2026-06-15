using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Stores billing information for an organization, used for invoicing and GST compliance.
/// </summary>
public class BillingDetails : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Billing organization name (may differ from org name for legal entity).
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>
    /// GST Identification Number (optional for B2C).
    /// </summary>
    [MaxLength(15)]
    public string? Gstin { get; set; }

    /// <summary>
    /// Billing address line 1.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Address line 2 (optional).
    /// </summary>
    [MaxLength(200)]
    public string? AddressLine2 { get; set; }

    /// <summary>
    /// City name.
    /// </summary>
    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// State code (2-letter) for GST purposes.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// State code (numeric) for place of supply determination.
    /// </summary>
    [MaxLength(2)]
    public string? StateCode { get; set; }

    /// <summary>
    /// PIN code.
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string Pincode { get; set; } = string.Empty;

    /// <summary>
    /// Country (defaults to India).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Country { get; set; } = "India";

    /// <summary>
    /// Contact email for billing communications.
    /// </summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Contact phone for billing.
    /// </summary>
    [MaxLength(20)]
    public string? Phone { get; set; }
}
