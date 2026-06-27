using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of the plan service.
/// </summary>
public class PlanService : IPlanService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<PlanService> _logger;

    private const string PlansCacheKey = "billing:plans:all";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private const decimal GstRate = 18.00m;

    public PlanService(
        ApplicationDbContext dbContext,
        IDistributedCache cache,
        ILogger<PlanService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PlansListResponse> GetAllPlansAsync()
    {
        var plans = await GetActivePlansAsync();

        var planDtos = plans.Select(MapToPlanDto).ToList();

        // Add-ons (could be stored in DB, but for now hardcoded as per spec)
        var addOns = new List<AddOnDto>
        {
            new("extra_notices_50", "Extra 50 Notices", "Additional notice quota", 49900, "monthly"),
            new("extra_user_seat", "Extra User Seat", "One additional user", 49900, "monthly"),
            new("extra_storage_20gb", "Extra 20GB Storage", "Additional storage", 19900, "monthly"),
            new("priority_processing", "Priority Processing", "Faster AI analysis", 99900, "monthly"),
            new("api_rate_increase", "API Rate Limit Increase", "10x API rate limit", 199900, "monthly")
        };

        return new PlansListResponse(planDtos, addOns);
    }

    public async Task<SubscriptionPlan?> GetPlanByCodeAsync(string code)
    {
        var plans = await GetActivePlansAsync();
        return plans.FirstOrDefault(p => p.Code == code);
    }

    public async Task<SubscriptionPlan?> GetPlanByIdAsync(Guid id)
    {
        return await _dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);
    }

    public async Task<PlanLimits?> GetPlanLimitsAsync(string planCode)
    {
        var plan = await GetPlanByCodeAsync(planCode);
        return plan?.Limits;
    }

    public async Task<SubscriptionPlan?> GetDefaultPlanAsync()
    {
        return await GetPlanByCodeAsync("free");
    }

    public SubscriptionPricingDto CalculateSubscriptionPrice(
        SubscriptionPlan plan,
        string billingCycle,
        int additionalSeats,
        int? discountAmount = null)
    {
        var baseAmount = billingCycle == BillingCycle.Annually
            ? plan.PricingAnnually ?? 0
            : plan.PricingMonthly ?? 0;

        var perSeatPrice = billingCycle == BillingCycle.Annually
            ? plan.PerSeatAnnually ?? 0
            : plan.PerSeatMonthly ?? 0;

        var additionalSeatsAmount = additionalSeats * perSeatPrice;
        var subtotal = baseAmount + additionalSeatsAmount - (discountAmount ?? 0);

        if (subtotal < 0) subtotal = 0;

        var gstAmount = (int)Math.Round(subtotal * GstRate / 100);
        var total = subtotal + gstAmount;

        return new SubscriptionPricingDto(
            BaseAmount: baseAmount,
            AdditionalSeatsAmount: additionalSeatsAmount,
            Subtotal: subtotal,
            GstRate: GstRate,
            GstAmount: gstAmount,
            Total: total,
            Currency: plan.Currency
        );
    }

    public int CalculateProration(
        SubscriptionPlan currentPlan,
        SubscriptionPlan newPlan,
        string currentCycle,
        string newCycle,
        int currentSeats,
        int newSeats,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var totalDays = (periodEnd - periodStart).TotalDays;
        var remainingDays = (periodEnd - DateTime.UtcNow).TotalDays;

        if (remainingDays <= 0 || totalDays <= 0)
            return 0;

        var remainingFraction = remainingDays / totalDays;

        // Calculate current plan value for remaining period
        var currentPricing = CalculateSubscriptionPrice(currentPlan, currentCycle, currentSeats);
        var currentRemainingValue = (int)(currentPricing.Subtotal * remainingFraction);

        // Calculate new plan value for remaining period
        var newPricing = CalculateSubscriptionPrice(newPlan, newCycle, newSeats);
        var newRemainingValue = (int)(newPricing.Subtotal * remainingFraction);

        // Proration amount (credit if negative, charge if positive)
        var prorationAmount = newRemainingValue - currentRemainingValue;

        // Add GST to proration
        var prorationWithGst = (int)(prorationAmount * (1 + GstRate / 100));

        return prorationWithGst;
    }

    public string GetPlanChangeType(
        SubscriptionPlan currentPlan,
        SubscriptionPlan newPlan,
        string currentCycle,
        string newCycle)
    {
        var currentPrice = currentCycle == BillingCycle.Annually
            ? currentPlan.PricingAnnually ?? 0
            : currentPlan.PricingMonthly ?? 0;

        var newPrice = newCycle == BillingCycle.Annually
            ? newPlan.PricingAnnually ?? 0
            : newPlan.PricingMonthly ?? 0;

        // Normalize to monthly for comparison
        if (currentCycle == BillingCycle.Annually)
            currentPrice /= 12;
        if (newCycle == BillingCycle.Annually)
            newPrice /= 12;

        return newPrice > currentPrice ? "upgrade" : "downgrade";
    }

    /// <summary>
    /// Gets extended plan pricing with annual savings calculation.
    /// Annual pricing applies 10/12 ratio (2 months free) discount.
    /// </summary>
    /// <param name="planCode">The plan code.</param>
    /// <returns>Extended pricing information including annual savings.</returns>
    public async Task<ExtendedPlanPricingDto?> GetPlanPricingAsync(string planCode)
    {
        var plan = await GetPlanByCodeAsync(planCode);
        if (plan == null)
            return null;

        return CalculateExtendedPricing(plan);
    }

    /// <summary>
    /// Calculates extended pricing with annual discount (2 months free = 10/12 ratio).
    /// </summary>
    private static ExtendedPlanPricingDto CalculateExtendedPricing(SubscriptionPlan plan)
    {
        var monthlyPrice = plan.PricingMonthly ?? 0;

        // Annual price should be 10 months worth (2 months free)
        // If stored annual price exists, use it; otherwise calculate
        var annualPrice = plan.PricingAnnually ?? (monthlyPrice * 10);

        // Calculate savings and discount percentage
        var fullYearPrice = monthlyPrice * 12;
        var annualSavings = fullYearPrice > 0 ? fullYearPrice - annualPrice : 0;
        var effectiveMonthlyRate = annualPrice > 0 ? annualPrice / 12 : 0;

        // Calculate discount percentage (should be ~16.67% for 2 months free)
        var annualDiscount = fullYearPrice > 0
            ? (int)Math.Round((1.0 - (double)annualPrice / fullYearPrice) * 100)
            : (int?)null;

        return new ExtendedPlanPricingDto(
            Monthly: plan.PricingMonthly,
            Annually: annualPrice > 0 ? annualPrice : null,
            Currency: plan.Currency,
            AnnualDiscount: annualDiscount,
            AnnualSavings: annualSavings > 0 ? annualSavings : null,
            EffectiveMonthlyRate: effectiveMonthlyRate > 0 ? effectiveMonthlyRate : null,
            PerSeat: plan.PerSeatMonthly.HasValue || plan.PerSeatAnnually.HasValue
                ? new PerSeatPricingDto(
                    plan.PerSeatMonthly,
                    plan.PerSeatAnnually ?? (plan.PerSeatMonthly.HasValue ? plan.PerSeatMonthly.Value * 10 : null))
                : null
        );
    }

    /// <summary>
    /// Calculates the annual price using the 10/12 discount (2 months free).
    /// </summary>
    /// <param name="monthlyPrice">The monthly price in paise.</param>
    /// <returns>The annual price with 2 months free discount.</returns>
    public static int CalculateAnnualPriceWithDiscount(int monthlyPrice)
    {
        // 2 months free = pay for 10 months
        return monthlyPrice * 10;
    }

    private async Task<List<SubscriptionPlan>> GetActivePlansAsync()
    {
        // Try cache first
        var cached = await _cache.GetStringAsync(PlansCacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var cachedPlans = JsonSerializer.Deserialize<List<SubscriptionPlan>>(cached);
                if (cachedPlans != null)
                    return cachedPlans;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached plans");
            }
        }

        // Fetch from database
        var plans = await _dbContext.SubscriptionPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        // Cache the results
        try
        {
            var serialized = JsonSerializer.Serialize(plans);
            await _cache.SetStringAsync(PlansCacheKey, serialized, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache plans");
        }

        return plans;
    }

    private static PlanDto MapToPlanDto(SubscriptionPlan plan)
    {
        var annualDiscount = plan.PricingMonthly.HasValue && plan.PricingAnnually.HasValue
            ? (int)Math.Round((1 - (plan.PricingAnnually.Value / 12.0) / plan.PricingMonthly.Value) * 100)
            : (int?)null;

        return new PlanDto(
            Id: plan.Id,
            Code: plan.Code,
            Name: plan.Name,
            DisplayName: plan.DisplayName,
            Description: plan.Description,
            Pricing: new PlanPricingDto(
                Monthly: plan.PricingMonthly,
                Annually: plan.PricingAnnually,
                Currency: plan.Currency,
                AnnualDiscount: annualDiscount,
                PerSeat: plan.PerSeatMonthly.HasValue || plan.PerSeatAnnually.HasValue
                    ? new PerSeatPricingDto(plan.PerSeatMonthly, plan.PerSeatAnnually)
                    : null
            ),
            Limits: new PlanLimitsDto(
                NoticesPerMonth: plan.Limits.NoticesPerMonth,
                Users: plan.Limits.Users,
                StorageGb: plan.Limits.StorageGb,
                OrganizationsCount: plan.Limits.OrganizationsCount,
                AdditionalUsersAllowed: plan.Limits.AdditionalUsersAllowed,
                ApiCalls: plan.Limits.ApiCalls
            ),
            Features: plan.Features,
            IsPopular: plan.IsPopular,
            TrialDays: plan.TrialDays,
            ContactSales: plan.ContactSales
        );
    }
}
