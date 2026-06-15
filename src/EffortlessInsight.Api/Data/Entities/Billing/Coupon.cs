using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Discount coupons for subscriptions.
/// </summary>
public class Coupon : BaseEntity
{
    /// <summary>
    /// Unique coupon code (e.g., LAUNCH20).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Discount type: percent or fixed
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string DiscountType { get; set; } = CouponDiscountType.Percent;

    /// <summary>
    /// Discount value. For percent: 0-100. For fixed: amount in paise.
    /// </summary>
    public int DiscountValue { get; set; }

    /// <summary>
    /// Maximum discount amount in paise (for percent discounts).
    /// </summary>
    public int? MaxDiscountAmount { get; set; }

    /// <summary>
    /// Maximum number of times this coupon can be redeemed. Null for unlimited.
    /// </summary>
    public int? MaxRedemptions { get; set; }

    /// <summary>
    /// Number of times this coupon has been redeemed.
    /// </summary>
    public int TimesRedeemed { get; set; }

    /// <summary>
    /// Plan codes this coupon applies to. ["*"] for all plans.
    /// </summary>
    public List<string> ApplicablePlans { get; set; } = ["*"];

    /// <summary>
    /// Billing cycles this coupon applies to. ["*"] for all cycles.
    /// </summary>
    public List<string> ApplicableCycles { get; set; } = ["*"];

    /// <summary>
    /// Number of billing cycles the discount applies. Null for first payment only.
    /// </summary>
    public int? DurationMonths { get; set; }

    /// <summary>
    /// Whether the coupon applies to recurring payments.
    /// </summary>
    public bool AppliesRecurring { get; set; }

    /// <summary>
    /// Minimum purchase amount in paise for coupon to apply.
    /// </summary>
    public int? MinPurchaseAmount { get; set; }

    /// <summary>
    /// When the coupon becomes valid.
    /// </summary>
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the coupon expires. Null for no expiry.
    /// </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// Whether the coupon is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether the coupon is only for first-time subscribers.
    /// </summary>
    public bool FirstTimeOnly { get; set; }

    /// <summary>
    /// Campaign or promotion this coupon is associated with.
    /// </summary>
    [MaxLength(100)]
    public string? Campaign { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    // Navigation properties
    public ICollection<CouponRedemption> Redemptions { get; set; } = [];
}

/// <summary>
/// Coupon discount type constants.
/// </summary>
public static class CouponDiscountType
{
    public const string Percent = "percent";
    public const string Fixed = "fixed";
}
