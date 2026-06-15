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
