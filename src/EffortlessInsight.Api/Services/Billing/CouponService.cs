using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of the coupon service.
/// </summary>
public class CouponService : ICouponService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPlanService _planService;
    private readonly ILogger<CouponService> _logger;

    public CouponService(
        ApplicationDbContext dbContext,
        IPlanService planService,
        ILogger<CouponService> logger)
    {
        _dbContext = dbContext;
        _planService = planService;
        _logger = logger;
    }

    public async Task<ValidateCouponResponse> ValidateCouponAsync(
        string code,
        string planCode,
        string billingCycle,
        Guid? organizationId = null)
    {
        var coupon = await GetCouponByCodeAsync(code);

        if (coupon == null)
        {
            return new ValidateCouponResponse(
                IsValid: false,
                ErrorMessage: "Invalid coupon code",
                Coupon: null
            );
        }

        // Check if coupon is active
        if (!coupon.IsActive)
        {
            return new ValidateCouponResponse(
                IsValid: false,
                ErrorMessage: "This coupon is no longer active",
                Coupon: null
            );
        }

        // Check validity period
        var now = DateTime.UtcNow;
        if (now < coupon.ValidFrom)
        {
            return new ValidateCouponResponse(
                IsValid: false,
                ErrorMessage: "This coupon is not yet valid",
                Coupon: null
            );
        }

        if (coupon.ValidUntil.HasValue && now > coupon.ValidUntil.Value)
        {
            return new ValidateCouponResponse(
                IsValid: false,
                ErrorMessage: "This coupon has expired",
                Coupon: null
            );
        }

        // Check redemption limits
        if (coupon.MaxRedemptions.HasValue && coupon.TimesRedeemed >= coupon.MaxRedemptions.Value)
        {
            return new ValidateCouponResponse(
                IsValid: false,
                ErrorMessage: "This coupon has reached its maximum redemptions",
                Coupon: null
            );
        }

        // Check applicable plans
        if (!coupon.ApplicablePlans.Contains("*") && !coupon.ApplicablePlans.Contains(planCode))
        {
            return new ValidateCouponResponse(
                IsValid: false,
                ErrorMessage: "This coupon is not valid for the selected plan",
                Coupon: null
            );
        }

        // Check applicable billing cycles
        if (!coupon.ApplicableCycles.Contains("*") && !coupon.ApplicableCycles.Contains(billingCycle))
        {
            return new ValidateCouponResponse(
                IsValid: false,
                ErrorMessage: "This coupon is not valid for the selected billing cycle",
                Coupon: null
            );
        }

        // Check if organization has already redeemed
        if (organizationId.HasValue)
        {
            var hasRedeemed = await HasRedeemedCouponAsync(coupon.Id, organizationId.Value);
            if (hasRedeemed)
            {
                return new ValidateCouponResponse(
                    IsValid: false,
                    ErrorMessage: "You have already used this coupon",
                    Coupon: null
                );
            }

            // Check first-time only restriction
            if (coupon.FirstTimeOnly)
            {
                var hasSubscription = await _dbContext.BillingSubscriptions
                    .AnyAsync(s => s.OrganizationId == organizationId.Value &&
                                   s.Status == SubscriptionStatus.Active);

                if (hasSubscription)
                {
                    return new ValidateCouponResponse(
                        IsValid: false,
                        ErrorMessage: "This coupon is only valid for first-time subscribers",
                        Coupon: null
                    );
                }
            }
        }

        // Calculate discount amount for display
        var plan = await _planService.GetPlanByCodeAsync(planCode);
        var planPrice = billingCycle == BillingCycle.Annually
            ? plan?.PricingAnnually ?? 0
            : plan?.PricingMonthly ?? 0;

        var calculatedDiscount = CalculateDiscount(coupon, planPrice);

        return new ValidateCouponResponse(
            IsValid: true,
            ErrorMessage: null,
            Coupon: new CouponDetailsDto(
                Code: coupon.Code,
                Description: coupon.Description,
                DiscountType: coupon.DiscountType,
                DiscountValue: coupon.DiscountValue,
                MaxDiscountAmount: coupon.MaxDiscountAmount,
                CalculatedDiscount: calculatedDiscount
            )
        );
    }

    public async Task<Coupon?> GetCouponByCodeAsync(string code)
    {
        return await _dbContext.Coupons
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == code.ToUpper() && c.IsActive);
    }

    public async Task<CouponRedemption> RedeemCouponAsync(
        Guid couponId,
        Guid organizationId,
        Guid? subscriptionId,
        Guid? invoiceId,
        int discountApplied,
        int originalAmount,
        int finalAmount,
        Guid? redeemedById = null)
    {
        var coupon = await _dbContext.Coupons.FindAsync(couponId)
            ?? throw new InvalidOperationException("Coupon not found");

        // Check if already redeemed
        if (await HasRedeemedCouponAsync(couponId, organizationId))
        {
            throw new InvalidOperationException("Coupon already redeemed by this organization");
        }

        var redemption = new CouponRedemption
        {
            CouponId = couponId,
            OrganizationId = organizationId,
            SubscriptionId = subscriptionId,
            InvoiceId = invoiceId,
            DiscountApplied = discountApplied,
            OriginalAmount = originalAmount,
            FinalAmount = finalAmount,
            RedeemedById = redeemedById,
            RedeemedAt = DateTime.UtcNow
        };

        _dbContext.CouponRedemptions.Add(redemption);

        // Increment redemption count
        coupon.TimesRedeemed++;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Coupon {CouponCode} redeemed by organization {OrganizationId}. Discount: {Discount}",
            coupon.Code, organizationId, discountApplied);

        return redemption;
    }

    public async Task<bool> HasRedeemedCouponAsync(Guid couponId, Guid organizationId)
    {
        return await _dbContext.CouponRedemptions
            .AnyAsync(r => r.CouponId == couponId && r.OrganizationId == organizationId);
    }

    public int CalculateDiscount(Coupon coupon, int purchaseAmount)
    {
        int discount;

        if (coupon.DiscountType == CouponDiscountType.Percent)
        {
            discount = (int)Math.Round(purchaseAmount * coupon.DiscountValue / 100.0);
        }
        else
        {
            discount = coupon.DiscountValue;
        }

        // Apply maximum discount cap
        if (coupon.MaxDiscountAmount.HasValue && discount > coupon.MaxDiscountAmount.Value)
        {
            discount = coupon.MaxDiscountAmount.Value;
        }

        // Check minimum purchase requirement
        if (coupon.MinPurchaseAmount.HasValue && purchaseAmount < coupon.MinPurchaseAmount.Value)
        {
            return 0;
        }

        return Math.Min(discount, purchaseAmount);
    }

    public async Task<Coupon> CreateCouponAsync(
        string code,
        string discountType,
        int discountValue,
        int? maxDiscountAmount = null,
        int? maxRedemptions = null,
        List<string>? applicablePlans = null,
        DateTime? validUntil = null,
        string? description = null,
        string? campaign = null)
    {
        // Check if code already exists
        var exists = await _dbContext.Coupons.AnyAsync(c => c.Code.ToUpper() == code.ToUpper());
        if (exists)
        {
            throw new InvalidOperationException($"Coupon code '{code}' already exists");
        }

        var coupon = new Coupon
        {
            Code = code.ToUpper(),
            Description = description,
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountAmount = maxDiscountAmount,
            MaxRedemptions = maxRedemptions,
            ApplicablePlans = applicablePlans ?? ["*"],
            ApplicableCycles = ["*"],
            ValidFrom = DateTime.UtcNow,
            ValidUntil = validUntil,
            IsActive = true,
            Campaign = campaign
        };

        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Created coupon {CouponCode} with {DiscountType} discount of {DiscountValue}",
            code, discountType, discountValue);

        return coupon;
    }

    public async Task DeactivateCouponAsync(Guid couponId)
    {
        var coupon = await _dbContext.Coupons.FindAsync(couponId);
        if (coupon != null)
        {
            coupon.IsActive = false;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Deactivated coupon {CouponId}", couponId);
        }
    }
}
