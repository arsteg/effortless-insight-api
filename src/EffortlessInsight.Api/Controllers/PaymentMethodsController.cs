using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for payment method management.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/payment-methods")]
public class PaymentMethodsController : ControllerBase
{
    private readonly IPaymentMethodService _paymentMethodService;
    private readonly ICurrentOrganizationService _currentOrganization;
    private readonly ILogger<PaymentMethodsController> _logger;

    public PaymentMethodsController(
        IPaymentMethodService paymentMethodService,
        ICurrentOrganizationService currentOrganization,
        ILogger<PaymentMethodsController> logger)
    {
        _paymentMethodService = paymentMethodService;
        _currentOrganization = currentOrganization;
        _logger = logger;
    }

    /// <summary>
    /// Get all payment methods for the current organization.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PaymentMethodListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaymentMethods()
    {
        var orgId = _currentOrganization.OrganizationId;
        if (orgId == null)
        {
            return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
        }

        var result = await _paymentMethodService.GetPaymentMethodsAsync(orgId.Value);
        return Ok(new ApiResponse<PaymentMethodListResponse>(true, result));
    }

    /// <summary>
    /// Get a specific payment method.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PaymentMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentMethod(Guid id)
    {
        var orgId = _currentOrganization.OrganizationId;
        if (orgId == null)
        {
            return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
        }

        var result = await _paymentMethodService.GetPaymentMethodAsync(orgId.Value, id);
        if (result == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Payment method not found"));
        }

        return Ok(new ApiResponse<PaymentMethodDto>(true, result));
    }

    /// <summary>
    /// Set a payment method as the default.
    /// </summary>
    [HttpPost("{id:guid}/set-default")]
    [ProducesResponseType(typeof(ApiResponse<PaymentMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDefaultPaymentMethod(Guid id)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _paymentMethodService.SetDefaultPaymentMethodAsync(orgId.Value, id);
            return Ok(new ApiResponse<PaymentMethodDto>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to set default payment method {PaymentMethodId}", id);
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Delete a payment method.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePaymentMethod(Guid id)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            if (orgId == null)
            {
                return BadRequest(new ApiErrorResponse(false, "NO_ORG", "No organization selected"));
            }

            var result = await _paymentMethodService.DeletePaymentMethodAsync(orgId.Value, id);
            if (!result)
            {
                return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Payment method not found"));
            }

            return Ok(new ApiResponse<object>(true, new { Message = "Payment method deleted successfully" }));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to delete payment method {PaymentMethodId}", id);
            return BadRequest(new ApiErrorResponse(false, "CANNOT_DELETE", ex.Message));
        }
    }
}
