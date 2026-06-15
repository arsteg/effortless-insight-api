using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for managing subscription plans.
/// </summary>
public interface IPlanService
{
    /// <summary>
    /// Gets all active subscription plans.
    /// </summary>
    Task<PlansListResponse> GetAllPlansAsync();

    /// <summary>
    /// Gets a plan by its code.
    /// </summary>
    Task<SubscriptionPlan?> GetPlanByCodeAsync(string code);

    /// <summary>
    /// Gets a plan by its ID.
    /// </summary>
    Task<SubscriptionPlan?> GetPlanByIdAsync(Guid id);

    /// <summary>
    /// Gets the plan limits for a specific plan.
    /// </summary>
    Task<PlanLimits?> GetPlanLimitsAsync(string planCode);

    /// <summary>
    /// Gets the default (free) plan.
    /// </summary>
    Task<SubscriptionPlan?> GetDefaultPlanAsync();

    /// <summary>
    /// Calculates the price for a subscription.
    /// </summary>
    SubscriptionPricingDto CalculateSubscriptionPrice(
        SubscriptionPlan plan,
        string billingCycle,
        int additionalSeats,
        int? discountAmount = null);

    /// <summary>
    /// Calculates proration for a plan change.
    /// </summary>
    int CalculateProration(
        SubscriptionPlan currentPlan,
        SubscriptionPlan newPlan,
        string currentCycle,
        string newCycle,
        int currentSeats,
        int newSeats,
        DateTime periodStart,
        DateTime periodEnd);

    /// <summary>
    /// Checks if a plan change is an upgrade or downgrade.
    /// </summary>
    string GetPlanChangeType(SubscriptionPlan currentPlan, SubscriptionPlan newPlan, string currentCycle, string newCycle);
}
