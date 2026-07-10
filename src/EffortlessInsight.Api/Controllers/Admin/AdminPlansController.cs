using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Admin;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing subscription plans.
/// </summary>
[Route("api/v1/admin/plans")]
[Authorize(AuthenticationSchemes = "AdminBearer")]
public class AdminPlansController : AdminControllerBase
{
    private readonly IPlanService _planService;
    private readonly IAdminAuditService _auditService;

    public AdminPlansController(
        IPlanService planService,
        IAdminAuditService auditService,
        ILogger<AdminPlansController> logger)
        : base(logger)
    {
        _planService = planService;
        _auditService = auditService;
    }

    /// <summary>
    /// Gets all subscription plans with search, filters, and pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminPlanListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPlans([FromQuery] PlanSearchParams searchParams)
    {
        if (!HasPermission(AdminPermissions.PlansManage))
        {
            return Forbid();
        }

        var result = await _planService.GetAllPlansForAdminAsync(searchParams);
        return Success(result);
    }

    /// <summary>
    /// Gets detailed information about a specific plan.
    /// </summary>
    [HttpGet("{planId:guid}")]
    [ProducesResponseType(typeof(AdminPlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPlan(Guid planId)
    {
        if (!HasPermission(AdminPermissions.PlansManage))
        {
            return Forbid();
        }

        var plan = await _planService.GetPlanDetailForAdminAsync(planId);
        if (plan == null)
        {
            return NotFoundResponse("Plan not found");
        }

        await _auditService.LogAsync(
            CurrentAdminId,
            "plan.viewed",
            "plan",
            planId.ToString(),
            $"Viewed plan {plan.Code}",
            null,
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success(plan);
    }

    /// <summary>
    /// Creates a new subscription plan.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminPlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequest request)
    {
        if (!HasPermission(AdminPermissions.PlansManage))
        {
            return Forbid();
        }

        try
        {
            var plan = await _planService.CreatePlanAsync(request, CurrentAdminId);

            await _auditService.LogAsync(
                CurrentAdminId,
                "plan.created",
                "plan",
                plan.Id.ToString(),
                $"Created plan {plan.Code}",
                new Dictionary<string, object>
                {
                    ["plan_code"] = plan.Code,
                    ["plan_name"] = plan.Name,
                    ["pricing_monthly"] = plan.PricingMonthly ?? 0,
                    ["pricing_annually"] = plan.PricingAnnually ?? 0,
                    ["is_active"] = plan.IsActive
                },
                ClientIpAddress,
                ClientUserAgent,
                CurrentSessionId);

            var detailedPlan = await _planService.GetPlanDetailForAdminAsync(plan.Id);
            return Success(detailedPlan, "Plan created successfully");
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message, "PLAN_CREATE_FAILED");
        }
    }

    /// <summary>
    /// Updates an existing subscription plan.
    /// </summary>
    [HttpPut("{planId:guid}")]
    [ProducesResponseType(typeof(AdminPlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdatePlan(Guid planId, [FromBody] UpdatePlanRequest request)
    {
        if (!HasPermission(AdminPermissions.PlansManage))
        {
            return Forbid();
        }

        try
        {
            var plan = await _planService.UpdatePlanAsync(planId, request, CurrentAdminId);

            await _auditService.LogAsync(
                CurrentAdminId,
                "plan.updated",
                "plan",
                plan.Id.ToString(),
                $"Updated plan {plan.Code}",
                new Dictionary<string, object>
                {
                    ["plan_code"] = plan.Code,
                    ["updates"] = request
                },
                ClientIpAddress,
                ClientUserAgent,
                CurrentSessionId);

            var detailedPlan = await _planService.GetPlanDetailForAdminAsync(plan.Id);
            return Success(detailedPlan, "Plan updated successfully");
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message, "PLAN_UPDATE_FAILED", 404);
        }
    }

    /// <summary>
    /// Soft deletes a subscription plan.
    /// </summary>
    [HttpDelete("{planId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeletePlan(Guid planId)
    {
        if (!HasPermission(AdminPermissions.PlansManage))
        {
            return Forbid();
        }

        try
        {
            // Get plan details before deletion for audit log
            var plan = await _planService.GetPlanDetailForAdminAsync(planId);
            if (plan == null)
            {
                return NotFoundResponse("Plan not found");
            }

            await _planService.DeletePlanAsync(planId, CurrentAdminId);

            await _auditService.LogAsync(
                CurrentAdminId,
                "plan.deleted",
                "plan",
                planId.ToString(),
                $"Deleted plan {plan.Code}",
                new Dictionary<string, object>
                {
                    ["plan_code"] = plan.Code,
                    ["plan_name"] = plan.Name,
                    ["subscriber_count"] = plan.SubscriberCount
                },
                ClientIpAddress,
                ClientUserAgent,
                CurrentSessionId);

            return Success<object?>(null, "Plan deleted successfully");
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message, "PLAN_DELETE_FAILED", 404);
        }
    }

    /// <summary>
    /// Activates a subscription plan.
    /// </summary>
    [HttpPatch("{planId:guid}/activate")]
    [ProducesResponseType(typeof(AdminPlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ActivatePlan(Guid planId)
    {
        if (!HasPermission(AdminPermissions.PlansManage))
        {
            return Forbid();
        }

        try
        {
            var plan = await _planService.ActivatePlanAsync(planId, CurrentAdminId);

            await _auditService.LogAsync(
                CurrentAdminId,
                "plan.activated",
                "plan",
                plan.Id.ToString(),
                $"Activated plan {plan.Code}",
                new Dictionary<string, object>
                {
                    ["plan_code"] = plan.Code
                },
                ClientIpAddress,
                ClientUserAgent,
                CurrentSessionId);

            var detailedPlan = await _planService.GetPlanDetailForAdminAsync(plan.Id);
            return Success(detailedPlan, "Plan activated successfully");
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message, "PLAN_ACTIVATE_FAILED", 404);
        }
    }

    /// <summary>
    /// Deactivates a subscription plan.
    /// </summary>
    [HttpPatch("{planId:guid}/deactivate")]
    [ProducesResponseType(typeof(AdminPlanDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeactivatePlan(Guid planId)
    {
        if (!HasPermission(AdminPermissions.PlansManage))
        {
            return Forbid();
        }

        try
        {
            var plan = await _planService.DeactivatePlanAsync(planId, CurrentAdminId);

            await _auditService.LogAsync(
                CurrentAdminId,
                "plan.deactivated",
                "plan",
                plan.Id.ToString(),
                $"Deactivated plan {plan.Code}",
                new Dictionary<string, object>
                {
                    ["plan_code"] = plan.Code
                },
                ClientIpAddress,
                ClientUserAgent,
                CurrentSessionId);

            var detailedPlan = await _planService.GetPlanDetailForAdminAsync(plan.Id);
            return Success(detailedPlan, "Plan deactivated successfully");
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message, "PLAN_DEACTIVATE_FAILED", 404);
        }
    }
}
