using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for usage tracking.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/usage")]
public class UsageController : ControllerBase
{
    private readonly IUsageService _usageService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ICurrentOrganizationService _currentOrganization;
    private readonly ILogger<UsageController> _logger;

    public UsageController(
        IUsageService usageService,
        ISubscriptionService subscriptionService,
        ICurrentOrganizationService currentOrganization,
        ILogger<UsageController> logger)
    {
        _usageService = usageService;
        _subscriptionService = subscriptionService;
        _currentOrganization = currentOrganization;
        _logger = logger;
    }

    /// <summary>
    /// Get current usage for the organization.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UsageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUsage()
    {
        var orgId = _currentOrganization.OrganizationId;
        if (orgId == null)
        {
            return NotFound(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
        }

        var subscription = await _subscriptionService.GetCurrentSubscriptionAsync(orgId.Value);
        if (subscription == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "No subscription found"));
        }

        return Ok(new ApiResponse<UsageDto>(true, subscription.Usage));
    }

    /// <summary>
    /// Check if a specific action can be performed (usage limit check).
    /// </summary>
    [HttpGet("check/{action}")]
    [ProducesResponseType(typeof(ApiResponse<UsageCheckResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckUsage(string action)
    {
        var orgId = _currentOrganization.OrganizationId;
        if (orgId == null)
        {
            return Ok(new ApiResponse<UsageCheckResponse>(true, new UsageCheckResponse(false, "No organization selected")));
        }

        (bool canPerform, string? reason) = action.ToLowerInvariant() switch
        {
            "notice" => await _usageService.CanCreateNoticeAsync(orgId.Value),
            "user" => await _usageService.CanAddUserAsync(orgId.Value),
            "api" => await _usageService.CanMakeApiCallAsync(orgId.Value),
            _ => (true, null)
        };

        return Ok(new ApiResponse<UsageCheckResponse>(true, new UsageCheckResponse(canPerform, reason)));
    }

    /// <summary>
    /// Get usage percentage for a specific metric.
    /// </summary>
    [HttpGet("percentage/{metric}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsagePercentage(string metric)
    {
        var orgId = _currentOrganization.OrganizationId;
        if (orgId == null)
        {
            return Ok(new ApiResponse<object>(true, new { percentage = 0 }));
        }

        var percentage = await _usageService.GetUsagePercentageAsync(orgId.Value, metric);
        return Ok(new ApiResponse<object>(true, new { percentage }));
    }
}

/// <summary>
/// Response for usage check.
/// </summary>
public record UsageCheckResponse(bool CanPerform, string? Reason);
