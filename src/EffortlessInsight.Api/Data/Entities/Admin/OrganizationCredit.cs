using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Credits applied to an organization's billing account.
/// Can be used for service compensation, promotional offers, etc.
/// </summary>
public class OrganizationCredit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Organization receiving the credit
    /// </summary>
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Admin who granted the credit
    /// </summary>
    public Guid GrantedById { get; set; }

    [ForeignKey(nameof(GrantedById))]
    public AdminUser GrantedBy { get; set; } = null!;

    /// <summary>
    /// Credit amount in paise (1/100 of INR)
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// Amount used so far (in paise)
    /// </summary>
    public int AmountUsed { get; set; }

    /// <summary>
    /// Remaining credit (computed: Amount - AmountUsed)
    /// </summary>
    [NotMapped]
    public int AmountRemaining => Amount - AmountUsed;

    /// <summary>
    /// Credit type: compensation, promotional, loyalty, adjustment
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string CreditType { get; set; } = "compensation";

    /// <summary>
    /// Reason for granting the credit
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Internal notes (not visible to customer)
    /// </summary>
    public string? InternalNotes { get; set; }

    /// <summary>
    /// Support ticket ID if applicable
    /// </summary>
    [MaxLength(100)]
    public string? TicketId { get; set; }

    /// <summary>
    /// Credit status: active, fully_used, expired, voided
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "active";

    /// <summary>
    /// When the credit expires (null = no expiry)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// When the credit was fully used
    /// </summary>
    public DateTime? FullyUsedAt { get; set; }

    /// <summary>
    /// If voided, admin who voided it
    /// </summary>
    public Guid? VoidedById { get; set; }

    /// <summary>
    /// Reason for voiding
    /// </summary>
    [MaxLength(500)]
    public string? VoidReason { get; set; }

    /// <summary>
    /// When voided
    /// </summary>
    public DateTime? VoidedAt { get; set; }

    /// <summary>
    /// Timestamps
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// History of credit usage
    /// </summary>
    public ICollection<CreditUsageRecord> UsageRecords { get; set; } = [];
}

/// <summary>
/// Records each usage of a credit
/// </summary>
public class CreditUsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationCreditId { get; set; }

    [ForeignKey(nameof(OrganizationCreditId))]
    public OrganizationCredit OrganizationCredit { get; set; } = null!;

    /// <summary>
    /// Invoice that used this credit
    /// </summary>
    public Guid? InvoiceId { get; set; }

    /// <summary>
    /// Amount used (in paise)
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// Description of usage
    /// </summary>
    [MaxLength(255)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Credit type constants
/// </summary>
public static class CreditTypes
{
    public const string Compensation = "compensation";
    public const string Promotional = "promotional";
    public const string Loyalty = "loyalty";
    public const string Adjustment = "adjustment";
    public const string Referral = "referral";
}

/// <summary>
/// Credit status constants
/// </summary>
public static class CreditStatus
{
    public const string Active = "active";
    public const string FullyUsed = "fully_used";
    public const string Expired = "expired";
    public const string Voided = "voided";
}
