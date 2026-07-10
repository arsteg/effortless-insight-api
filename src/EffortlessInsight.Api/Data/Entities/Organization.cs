using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class Organization : BaseEntity
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Lowercase, trimmed name for case-insensitive uniqueness
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string NameNormalized { get; set; } = string.Empty;

    /// <summary>
    /// Registered legal name of the organization
    /// </summary>
    [MaxLength(255)]
    public string? LegalName { get; set; }

    /// <summary>
    /// Short display name for UI
    /// </summary>
    [MaxLength(100)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Legacy GSTIN storage - to be removed after migration
    /// </summary>
    [Obsolete("Use OrganizationGstins navigation property instead")]
    public List<string> Gstins { get; set; } = [];

    [MaxLength(100)]
    public string? Industry { get; set; }

    [MaxLength(100)]
    public string? SubIndustry { get; set; }

    /// <summary>
    /// Business type: proprietorship, partnership, llp, pvt_ltd, public_ltd, trust, society, other
    /// </summary>
    [MaxLength(50)]
    public string? BusinessType { get; set; }

    /// <summary>
    /// Annual turnover range: 0-40L, 40L-1.5Cr, 1.5Cr-5Cr, 5Cr-25Cr, 25Cr+
    /// </summary>
    [MaxLength(50)]
    public string? AnnualTurnoverRange { get; set; }

    /// <summary>
    /// Employee count range: 1-10, 11-50, 51-200, 200+
    /// </summary>
    [MaxLength(20)]
    public string? EmployeeCountRange { get; set; }

    /// <summary>
    /// Legacy field - kept for backward compatibility
    /// </summary>
    public decimal? AnnualTurnover { get; set; }

    /// <summary>
    /// Legacy field - kept for backward compatibility
    /// </summary>
    public int? EmployeeCount { get; set; }

    /// <summary>
    /// Organization contact email
    /// </summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Organization contact phone
    /// </summary>
    [MaxLength(20)]
    public string? Phone { get; set; }

    /// <summary>
    /// Organization website URL
    /// </summary>
    [MaxLength(255)]
    public string? Website { get; set; }

    [MaxLength(50)]
    public string? State { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// Address line 1
    /// </summary>
    [MaxLength(255)]
    public string? AddressLine1 { get; set; }

    /// <summary>
    /// Address line 2
    /// </summary>
    [MaxLength(255)]
    public string? AddressLine2 { get; set; }

    /// <summary>
    /// Legacy address field - kept for backward compatibility
    /// </summary>
    public string? Address { get; set; }

    [MaxLength(10)]
    public string? PinCode { get; set; }

    [MaxLength(50)]
    public string Country { get; set; } = "India";

    /// <summary>
    /// Permanent Account Number
    /// </summary>
    [MaxLength(10)]
    public string? Pan { get; set; }

    /// <summary>
    /// Tax Deduction Account Number
    /// </summary>
    [MaxLength(10)]
    public string? Tan { get; set; }

    [MaxLength(20)]
    public string SubscriptionStatus { get; set; } = "trial";

    public DateTime? TrialEndsAt { get; set; }

    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// Organization-level settings stored as JSONB
    /// </summary>
    public Dictionary<string, object>? Settings { get; set; }

    /// <summary>
    /// Organization logo URL (S3)
    /// </summary>
    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Brand color for enterprise white-labeling (hex format)
    /// </summary>
    [MaxLength(7)]
    public string? BrandColor { get; set; }

    // Navigation properties

    /// <summary>
    /// Legacy: Users directly associated with organization
    /// </summary>
    public ICollection<ApplicationUser> Users { get; set; } = [];

    /// <summary>
    /// Organization memberships (supports multi-org per user)
    /// </summary>
    public ICollection<OrganizationMember> Members { get; set; } = [];

    /// <summary>
    /// GSTINs registered under this organization
    /// </summary>
    public ICollection<OrganizationGstin> OrganizationGstins { get; set; } = [];

    /// <summary>
    /// Pending invitations to join this organization
    /// </summary>
    public ICollection<OrganizationInvitation> Invitations { get; set; } = [];

    public ICollection<Notice> Notices { get; set; } = [];

    /// <summary>
    /// Custom roles defined for this organization
    /// </summary>
    public ICollection<CustomRole> CustomRoles { get; set; } = [];

    /// <summary>
    /// Teams/departments in this organization
    /// </summary>
    public ICollection<Team> Teams { get; set; } = [];
}
