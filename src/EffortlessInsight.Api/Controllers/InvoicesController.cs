using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for invoice management.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;
    private readonly ICurrentOrganizationService _currentOrganization;
    private readonly ILogger<InvoicesController> _logger;

    public InvoicesController(
        IInvoiceService invoiceService,
        ICurrentOrganizationService currentOrganization,
        ILogger<InvoicesController> logger)
    {
        _invoiceService = invoiceService;
        _currentOrganization = currentOrganization;
        _logger = logger;
    }

    /// <summary>
    /// Get organization's invoices.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<InvoiceListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvoices(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10)
    {
        var orgId = _currentOrganization.OrganizationId;
        if (orgId == null)
        {
            return Ok(new ApiResponse<InvoiceListResponse>(true, new InvoiceListResponse(
                [], new BillingPaginationDto(1, limit, 0, 0))));
        }

        limit = Math.Min(limit, 50);
        var result = await _invoiceService.GetInvoicesAsync(orgId.Value, page, limit);
        return Ok(new ApiResponse<InvoiceListResponse>(true, result));
    }

    /// <summary>
    /// Get a specific invoice.
    /// </summary>
    [HttpGet("{invoiceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<InvoiceDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoice(Guid invoiceId)
    {
        var orgId = _currentOrganization.OrganizationId;
        if (orgId == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Invoice not found"));
        }

        var invoice = await _invoiceService.GetInvoiceByIdAsync(invoiceId, orgId.Value);
        if (invoice == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Invoice not found"));
        }

        return Ok(new ApiResponse<InvoiceDetailDto>(true, invoice));
    }

    /// <summary>
    /// Download invoice PDF.
    /// </summary>
    [HttpGet("{invoiceId:guid}/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoicePdf(Guid invoiceId)
    {
        try
        {
            var orgId = _currentOrganization.OrganizationId;
            if (orgId == null)
            {
                return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Invoice not found"));
            }

            var pdf = await _invoiceService.GetInvoicePdfAsync(invoiceId, orgId.Value);
            return File(pdf, "application/pdf", $"invoice-{invoiceId}.pdf");
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Invoice not found"));
        }
    }
}
