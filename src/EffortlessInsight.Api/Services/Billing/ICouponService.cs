using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for managing coupons and discounts.
/// </summary>
public interface ICouponService
{
    /// <summary>
    /// Validates a coupon code for a specific plan and billing cycle.
    /// </summary>
    Task<ValidateCouponResponse> ValidateCouponAsync(
        string code,
        string planCode,
        string billingCycle,
        Guid? organizationId = null);

    /// <summary>
    /// Gets a coupon by its code.
    /// </summary>
    Task<Coupon?> GetCouponByCodeAsync(string code);

    /// <summary>
    /// Redeems a coupon for an organization.
    /// </summary>
    Task<CouponRedemption> RedeemCouponAsync(
        Guid couponId,
        Guid organizationId,
        Guid? subscriptionId,
        Guid? invoiceId,
        int discountApplied,
        int originalAmount,
        int finalAmount,
        Guid? redeemedById = null);

    /// <summary>
    /// Checks if an organization has already redeemed a coupon.
    /// </summary>
    Task<bool> HasRedeemedCouponAsync(Guid couponId, Guid organizationId);

    /// <summary>
    /// Calculates the discount amount for a coupon.
    /// </summary>
    int CalculateDiscount(Coupon coupon, int purchaseAmount);

    /// <summary>
    /// Creates a new coupon.
    /// </summary>
    Task<Coupon> CreateCouponAsync(
        string code,
        string discountType,
        int discountValue,
        int? maxDiscountAmount = null,
        int? maxRedemptions = null,
        List<string>? applicablePlans = null,
        DateTime? validUntil = null,
        string? description = null,
        string? campaign = null);

    /// <summary>
    /// Deactivates a coupon.
    /// </summary>
    Task DeactivateCouponAsync(Guid couponId);
}
