using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for subscription plans.
/// </summary>
[ApiController]
[Route("api/v1/plans")]
public class PlansController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly ILogger<PlansController> _logger;

    public PlansController(
        IPlanService planService,
        ILogger<PlansController> logger)
    {
        _planService = planService;
        _logger = logger;
    }

    /// <summary>
    /// Get all available subscription plans.
    /// </summary>
    /// <remarks>
    /// Returns all active plans with pricing, limits, and features.
    /// This endpoint is public and does not require authentication.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PlansListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _planService.GetAllPlansAsync();
        return Ok(new ApiResponse<PlansListResponse>(true, plans));
    }

    /// <summary>
    /// Get a specific plan by code.
    /// </summary>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(ApiResponse<PlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanByCode(string code)
    {
        var plan = await _planService.GetPlanByCodeAsync(code);
        if (plan == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", $"Plan '{code}' not found"));
        }

        // Map to DTO
        var planDto = MapToPlanDto(plan);
        return Ok(new ApiResponse<PlanDto>(true, planDto));
    }

    /// <summary>
    /// Get plan pricing for comparison.
    /// </summary>
    [HttpGet("compare")]
    [ProducesResponseType(typeof(ApiResponse<PlansListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ComparePlans()
    {
        var plans = await _planService.GetAllPlansAsync();
        return Ok(new ApiResponse<PlansListResponse>(true, plans));
    }

    private static PlanDto MapToPlanDto(Data.Entities.Billing.SubscriptionPlan plan)
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
