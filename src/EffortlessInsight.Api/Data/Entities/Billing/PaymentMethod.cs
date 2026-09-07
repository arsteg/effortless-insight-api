using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Stores saved payment methods for recurring billing.
/// </summary>
public class PaymentMethod : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Payment method type: card, upi, netbanking, wallet
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = PaymentMethodType.Card;

    /// <summary>
    /// Whether this is the default payment method for the organization.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether this payment method is active and can be used.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // Card-specific fields
    /// <summary>
    /// Last 4 digits of the card number.
    /// </summary>
    [MaxLength(4)]
    public string? CardLast4 { get; set; }

    /// <summary>
    /// Card brand (visa, mastercard, rupay, etc.).
    /// </summary>
    [MaxLength(20)]
    public string? CardBrand { get; set; }

    /// <summary>
    /// Card expiry month (1-12).
    /// </summary>
    public int? CardExpiryMonth { get; set; }

    /// <summary>
    /// Card expiry year (full year, e.g., 2025).
    /// </summary>
    public int? CardExpiryYear { get; set; }

    /// <summary>
    /// Cardholder name.
    /// </summary>
    [MaxLength(100)]
    public string? CardName { get; set; }

    /// <summary>
    /// Card funding type: credit, debit, prepaid.
    /// </summary>
    [MaxLength(20)]
    public string? CardFunding { get; set; }

    // UPI-specific fields
    /// <summary>
    /// UPI ID (VPA).
    /// </summary>
    [MaxLength(100)]
    public string? UpiId { get; set; }

    // Razorpay references
    /// <summary>
    /// Razorpay token ID for recurring payments.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayTokenId { get; set; }

    /// <summary>
    /// Razorpay customer ID.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayCustomerId { get; set; }

    // Mandate fields (for UPI AutoPay and NetBanking SI)
    /// <summary>
    /// Razorpay mandate ID for e-mandate/auto-debit.
    /// </summary>
    [MaxLength(50)]
    public string? MandateId { get; set; }

    /// <summary>
    /// Mandate status: pending, authenticated, active, cancelled, expired.
    /// </summary>
    [MaxLength(20)]
    public string? MandateStatus { get; set; }

    /// <summary>
    /// Maximum amount that can be debited per mandate cycle (in paise).
    /// </summary>
    public int? MandateMaxAmount { get; set; }

    /// <summary>
    /// When the mandate expires.
    /// </summary>
    public DateTime? MandateExpiresAt { get; set; }

    /// <summary>
    /// When the payment method was last used.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Payment method type constants.
/// </summary>
public static class PaymentMethodType
{
    public const string Card = "card";
    public const string Upi = "upi";
    public const string NetBanking = "netbanking";
    public const string Wallet = "wallet";
}

/// <summary>
/// Mandate status constants for UPI AutoPay and NetBanking SI.
/// </summary>
public static class MandateStatusType
{
    /// <summary>
    /// Mandate created but not yet authenticated by customer.
    /// </summary>
    public const string Pending = "pending";

    /// <summary>
    /// Customer has completed authentication, mandate is being processed.
    /// </summary>
    public const string Authenticated = "authenticated";

    /// <summary>
    /// Mandate is active and can be used for auto-debit.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// Mandate was cancelled by customer or bank.
    /// </summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Mandate has expired.
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// Mandate creation/authentication failed.
    /// </summary>
    public const string Failed = "failed";
}
