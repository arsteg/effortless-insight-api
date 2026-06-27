using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for subscription management.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/subscriptions")]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ICouponService _couponService;
    private readonly ICurrentOrganizationService _currentOrganization;
    private readonly ILogger<SubscriptionsController> _logger;

    public SubscriptionsController(
        ISubscriptionService subscriptionService,
        ICouponService couponService,
        ICurrentOrganizationService currentOrganization,
        ILogger<SubscriptionsController> logger)
    {
        _subscriptionService = subscriptionService;
        _couponService = couponService;
        _currentOrganization = currentOrganization;
        _logger = logger;
    }

    /// <summary>
    /// Get current organization's subscription.
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiResponse<CurrentSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentSubscription()
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

        return Ok(new ApiResponse<CurrentSubscriptionResponse>(true, subscription));
    }

    /// <summary>
    /// Create a new subscription (initiate checkout).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionRequest request)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            var userId = GetCurrentUserId();

            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _subscriptionService.CreateSubscriptionAsync(orgId.Value, userId, request);
            return Ok(new ApiResponse<CreateSubscriptionResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "CREATE_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Verify payment and activate subscription.
    /// </summary>
    [HttpPost("verify")]
    [ProducesResponseType(typeof(ApiResponse<VerifyPaymentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest request)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            var userId = GetCurrentUserId();

            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _subscriptionService.VerifyPaymentAsync(orgId.Value, userId, request);
            return Ok(new ApiResponse<VerifyPaymentResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "VERIFY_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Change subscription plan (upgrade/downgrade).
    /// </summary>
    [HttpPut("current/plan")]
    [ProducesResponseType(typeof(ApiResponse<ChangePlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            var userId = GetCurrentUserId();

            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _subscriptionService.ChangePlanAsync(orgId.Value, userId, request);
            return Ok(new ApiResponse<ChangePlanResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "CHANGE_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Cancel subscription.
    /// </summary>
    [HttpDelete("current")]
    [ProducesResponseType(typeof(ApiResponse<CancelSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelSubscription([FromBody] CancelSubscriptionRequest request)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            var userId = GetCurrentUserId();

            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _subscriptionService.CancelSubscriptionAsync(orgId.Value, userId, request);
            return Ok(new ApiResponse<CancelSubscriptionResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "CANCEL_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Add additional seats to subscription.
    /// </summary>
    [HttpPost("current/seats")]
    [ProducesResponseType(typeof(ApiResponse<AddSeatsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSeats([FromBody] AddSeatsRequest request)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            var userId = GetCurrentUserId();

            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _subscriptionService.AddSeatsAsync(orgId.Value, userId, request);
            return Ok(new ApiResponse<AddSeatsResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "ADD_SEATS_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Reactivate a cancelled subscription.
    /// </summary>
    [HttpPost("current/reactivate")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReactivateSubscription()
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            var userId = GetCurrentUserId();

            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _subscriptionService.ReactivateSubscriptionAsync(orgId.Value, userId);
            return Ok(new ApiResponse<SubscriptionDto>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "REACTIVATE_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Validate a coupon code.
    /// </summary>
    [HttpPost("/api/v1/coupons/validate")]
    [ProducesResponseType(typeof(ApiResponse<ValidateCouponResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateCoupon([FromBody] ValidateCouponRequest request)
    {
        var orgId = _currentOrganization.OrganizationId;
        var result = await _couponService.ValidateCouponAsync(
            request.Code,
            request.PlanCode,
            request.BillingCycle,
            orgId);

        return Ok(new ApiResponse<ValidateCouponResponse>(true, result));
    }

    /// <summary>
    /// Create a refund for a subscription payment.
    /// </summary>
    /// <remarks>
    /// Creates a partial or full refund for the most recent payment on the specified subscription.
    /// If amount is not specified, the full payment amount will be refunded.
    /// </remarks>
    /// <param name="subscriptionId">The subscription ID to refund.</param>
    /// <param name="request">The refund request details.</param>
    /// <returns>Refund details including the refund ID and status.</returns>
    [HttpPost("{subscriptionId}/refund")]
    [ProducesResponseType(typeof(ApiResponse<RefundResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateRefund(
        [FromRoute] Guid subscriptionId,
        [FromBody] CreateRefundRequest request)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            // Verify the subscription belongs to the current organization
            var subscription = await _subscriptionService.GetSubscriptionEntityAsync(orgId.Value);
            if (subscription == null || subscription.Id != subscriptionId)
            {
                return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Subscription not found"));
            }

            var result = await _subscriptionService.CreateRefundAsync(
                subscriptionId,
                request.Amount,
                request.Reason);

            _logger.LogInformation(
                "Refund {RefundId} created for subscription {SubscriptionId}. Amount: {Amount}",
                result.RefundId, subscriptionId, result.Amount);

            return Ok(new ApiResponse<RefundResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "REFUND_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Manually retry the last failed payment for the current subscription.
    /// </summary>
    /// <remarks>
    /// This endpoint allows users to manually retry a failed payment when their subscription
    /// is in 'past_due' status. A valid default payment method must be on file.
    /// </remarks>
    /// <returns>Payment retry result with the new subscription status.</returns>
    [HttpPost("current/retry-payment")]
    [ProducesResponseType(typeof(ApiResponse<PaymentRetryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RetryPayment()
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _subscriptionService.RetryPaymentAsync(orgId.Value);

            _logger.LogInformation(
                "Payment retry for organization {OrganizationId}: Success={Success}",
                orgId.Value, result.Success);

            return Ok(new ApiResponse<PaymentRetryResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "RETRY_FAILED", ex.Message));
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue("sub");
        return Guid.Parse(userIdClaim!);
    }
}
