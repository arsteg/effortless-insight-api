using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Represents an organization's subscription to a plan.
/// This is an enhanced version of the basic Subscription entity with full lifecycle support.
/// </summary>
public class BillingSubscription : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string PlanCode { get; set; } = string.Empty;

    public Guid PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;

    /// <summary>
    /// Subscription status: trialing, active, past_due, cancelled, expired
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = SubscriptionStatus.Trialing;

    /// <summary>
    /// Billing cycle: monthly or annually
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string BillingCycle { get; set; } = "monthly";

    /// <summary>
    /// Number of seats included in the base plan.
    /// </summary>
    public int SeatsIncluded { get; set; }

    /// <summary>
    /// Number of additional purchased seats.
    /// </summary>
    public int SeatsAdditional { get; set; }

    /// <summary>
    /// Start of the current billing period.
    /// </summary>
    public DateTime CurrentPeriodStart { get; set; }

    /// <summary>
    /// End of the current billing period.
    /// </summary>
    public DateTime CurrentPeriodEnd { get; set; }

    /// <summary>
    /// When the trial started.
    /// </summary>
    public DateTime? TrialStart { get; set; }

    /// <summary>
    /// When the trial ends.
    /// </summary>
    public DateTime? TrialEnd { get; set; }

    /// <summary>
    /// Whether to cancel at the end of the current period.
    /// </summary>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>
    /// When the subscription was cancelled.
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Reason for cancellation.
    /// </summary>
    [MaxLength(50)]
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Additional feedback from the user about cancellation.
    /// </summary>
    public string? CancellationFeedback { get; set; }

    /// <summary>
    /// When the subscription ended (cancelled or expired).
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Scheduled plan for downgrade (applied at period end).
    /// </summary>
    [MaxLength(50)]
    public string? ScheduledPlanCode { get; set; }

    /// <summary>
    /// Scheduled billing cycle for plan change.
    /// </summary>
    [MaxLength(10)]
    public string? ScheduledBillingCycle { get; set; }

    /// <summary>
    /// When scheduled changes take effect.
    /// </summary>
    public DateTime? ScheduledChangeDate { get; set; }

    /// <summary>
    /// Razorpay subscription ID.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpaySubscriptionId { get; set; }

    /// <summary>
    /// Razorpay customer ID.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayCustomerId { get; set; }

    /// <summary>
    /// Number of failed payment attempts.
    /// </summary>
    public int FailedPaymentAttempts { get; set; }

    /// <summary>
    /// Payment retry count (alias for FailedPaymentAttempts for clarity).
    /// </summary>
    public int PaymentRetryCount
    {
        get => FailedPaymentAttempts;
        set => FailedPaymentAttempts = value;
    }

    /// <summary>
    /// When to attempt the next payment retry.
    /// </summary>
    public DateTime? NextPaymentRetryAt { get; set; }

    /// <summary>
    /// Last payment failure date.
    /// </summary>
    public DateTime? LastPaymentFailedAt { get; set; }

    /// <summary>
    /// Grace period end date after payment failure.
    /// </summary>
    public DateTime? GracePeriodEndAt { get; set; }

    /// <summary>
    /// Base amount before taxes and additional seats.
    /// </summary>
    public decimal BaseAmount { get; set; }

    /// <summary>
    /// Amount for additional seats.
    /// </summary>
    public decimal AdditionalSeatsAmount { get; set; }

    /// <summary>
    /// Tax amount (GST).
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Total amount including tax.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Currency code (default INR).
    /// </summary>
    [MaxLength(3)]
    public string Currency { get; set; } = "INR";

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    // Navigation properties
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<CouponRedemption> CouponRedemptions { get; set; } = [];
}

/// <summary>
/// Subscription status constants.
/// </summary>
public static class SubscriptionStatus
{
    public const string Trialing = "trialing";
    public const string Active = "active";
    public const string PastDue = "past_due";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
}

/// <summary>
/// Billing cycle constants.
/// </summary>
public static class BillingCycle
{
    public const string Monthly = "monthly";
    public const string Annually = "annually";
}
