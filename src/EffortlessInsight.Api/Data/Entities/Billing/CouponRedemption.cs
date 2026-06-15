using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Tracks coupon usage by organizations.
/// </summary>
public class CouponRedemption : BaseEntity
{
    [Required]
    public Guid CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public Guid? SubscriptionId { get; set; }
    public BillingSubscription? Subscription { get; set; }

    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    /// <summary>
    /// Discount amount applied in paise.
    /// </summary>
    public int DiscountApplied { get; set; }

    /// <summary>
    /// When the coupon was redeemed.
    /// </summary>
    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who redeemed the coupon.
    /// </summary>
    public Guid? RedeemedById { get; set; }
    public ApplicationUser? RedeemedBy { get; set; }

    /// <summary>
    /// Original purchase amount before discount.
    /// </summary>
    public int OriginalAmount { get; set; }

    /// <summary>
    /// Final amount after discount.
    /// </summary>
    public int FinalAmount { get; set; }
}
