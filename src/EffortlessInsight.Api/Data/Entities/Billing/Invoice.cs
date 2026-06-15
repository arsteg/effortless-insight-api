using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// GST-compliant invoice for billing transactions.
/// </summary>
public class Invoice : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public Guid? SubscriptionId { get; set; }
    public BillingSubscription? Subscription { get; set; }

    /// <summary>
    /// Unique invoice number (e.g., INV-2024-001234).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Invoice status: draft, pending, paid, void, refunded
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = InvoiceStatus.Draft;

    /// <summary>
    /// Date the invoice was issued.
    /// </summary>
    public DateOnly InvoiceDate { get; set; }

    /// <summary>
    /// Payment due date.
    /// </summary>
    public DateOnly DueDate { get; set; }

    /// <summary>
    /// Currency code.
    /// </summary>
    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "INR";

    /// <summary>
    /// Subtotal before tax (in paise).
    /// </summary>
    public int Subtotal { get; set; }

    /// <summary>
    /// Discount amount (in paise).
    /// </summary>
    public int Discount { get; set; }

    /// <summary>
    /// Discount description (e.g., coupon code).
    /// </summary>
    [MaxLength(100)]
    public string? DiscountDescription { get; set; }

    /// <summary>
    /// GST tax rate (e.g., 18.00).
    /// </summary>
    public decimal TaxRate { get; set; } = 18.00m;

    /// <summary>
    /// Total tax amount (in paise).
    /// </summary>
    public int TaxAmount { get; set; }

    /// <summary>
    /// CGST amount for intra-state (in paise).
    /// </summary>
    public int? CgstAmount { get; set; }

    /// <summary>
    /// SGST amount for intra-state (in paise).
    /// </summary>
    public int? SgstAmount { get; set; }

    /// <summary>
    /// IGST amount for inter-state (in paise).
    /// </summary>
    public int? IgstAmount { get; set; }

    /// <summary>
    /// Total invoice amount including tax (in paise).
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Amount already paid (in paise).
    /// </summary>
    public int AmountPaid { get; set; }

    /// <summary>
    /// Remaining amount due (in paise).
    /// </summary>
    public int AmountDue { get; set; }

    /// <summary>
    /// Invoice description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// HSN/SAC code for the service.
    /// </summary>
    [MaxLength(10)]
    public string HsnCode { get; set; } = "998314";

    /// <summary>
    /// Place of supply (state code).
    /// </summary>
    [MaxLength(50)]
    public string? PlaceOfSupply { get; set; }

    /// <summary>
    /// Place of supply state code.
    /// </summary>
    [MaxLength(2)]
    public string? PlaceOfSupplyCode { get; set; }

    /// <summary>
    /// Whether this is an intra-state or inter-state transaction.
    /// </summary>
    public bool IsInterState { get; set; }

    /// <summary>
    /// Billing details snapshot at time of invoice.
    /// </summary>
    public InvoiceBillingDetails BillingDetails { get; set; } = new();

    /// <summary>
    /// Additional notes on the invoice.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Razorpay invoice ID.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayInvoiceId { get; set; }

    /// <summary>
    /// URL to the PDF invoice (S3).
    /// </summary>
    [MaxLength(500)]
    public string? PdfUrl { get; set; }

    /// <summary>
    /// When the invoice was paid.
    /// </summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// When the invoice was voided.
    /// </summary>
    public DateTime? VoidedAt { get; set; }

    /// <summary>
    /// Reason for voiding.
    /// </summary>
    public string? VoidReason { get; set; }

    // Navigation properties
    public ICollection<InvoiceLineItem> LineItems { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

/// <summary>
/// Invoice status constants.
/// </summary>
public static class InvoiceStatus
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Void = "void";
    public const string Refunded = "refunded";
}

/// <summary>
/// Snapshot of billing details at time of invoice creation.
/// </summary>
public class InvoiceBillingDetails
{
    public string OrganizationName { get; set; } = string.Empty;
    public string? Gstin { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string State { get; set; } = string.Empty;
    public string? StateCode { get; set; }
    public string Pincode { get; set; } = string.Empty;
    public string Country { get; set; } = "India";
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
