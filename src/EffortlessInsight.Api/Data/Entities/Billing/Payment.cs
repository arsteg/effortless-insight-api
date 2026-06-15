using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Records payment transactions.
/// </summary>
public class Payment : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public Guid? SubscriptionId { get; set; }
    public BillingSubscription? Subscription { get; set; }

    /// <summary>
    /// Payment amount in paise.
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// Currency code.
    /// </summary>
    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "INR";

    /// <summary>
    /// Payment status: pending, authorized, captured, failed, refunded
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// Payment method used: card, upi, netbanking, wallet
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PaymentMethod { get; set; } = PaymentMethodType.Card;

    /// <summary>
    /// Payment method details (e.g., card last 4, bank name).
    /// </summary>
    [MaxLength(100)]
    public string? PaymentMethodDetails { get; set; }

    /// <summary>
    /// Razorpay payment ID.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayPaymentId { get; set; }

    /// <summary>
    /// Razorpay order ID.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayOrderId { get; set; }

    /// <summary>
    /// Razorpay signature for verification.
    /// </summary>
    [MaxLength(200)]
    public string? RazorpaySignature { get; set; }

    /// <summary>
    /// Failure reason if payment failed.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Failure code from payment gateway.
    /// </summary>
    [MaxLength(50)]
    public string? FailureCode { get; set; }

    /// <summary>
    /// Refund ID if refunded.
    /// </summary>
    [MaxLength(50)]
    public string? RefundId { get; set; }

    /// <summary>
    /// Amount refunded in paise.
    /// </summary>
    public int? RefundAmount { get; set; }

    /// <summary>
    /// When the refund was processed.
    /// </summary>
    public DateTime? RefundedAt { get; set; }

    /// <summary>
    /// Reason for refund.
    /// </summary>
    public string? RefundReason { get; set; }

    /// <summary>
    /// When the payment was captured.
    /// </summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>
    /// Receipt number for this payment.
    /// </summary>
    [MaxLength(50)]
    public string? ReceiptNumber { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Payment status constants.
/// </summary>
public static class PaymentStatus
{
    public const string Pending = "pending";
    public const string Authorized = "authorized";
    public const string Captured = "captured";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
    public const string PartialRefund = "partial_refund";
}
