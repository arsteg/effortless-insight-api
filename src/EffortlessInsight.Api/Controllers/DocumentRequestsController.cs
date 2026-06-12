using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Collaboration;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1")]
public class DocumentRequestsController : ControllerBase
{
    private readonly IDocumentRequestService _documentRequestService;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<DocumentRequestsController> _logger;

    public DocumentRequestsController(
        IDocumentRequestService documentRequestService,
        ICurrentOrganizationService orgService,
        ILogger<DocumentRequestsController> logger)
    {
        _documentRequestService = documentRequestService;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ==========================================================================
    // Notice-scoped Document Request Endpoints
    // ==========================================================================

    /// <summary>
    /// Get all document requests for a notice
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/document-requests")]
    [ProducesResponseType(typeof(DocumentRequestListResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocumentRequestsForNotice(
        Guid noticeId,
        [FromQuery] string? status = null)
    {
        try
        {
            var result = await _documentRequestService.GetDocumentRequestsForNoticeAsync(
                noticeId, GetUserId(), status);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create a document request
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/document-requests")]
    [ProducesResponseType(typeof(DocumentRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDocumentRequest(
        Guid noticeId,
        [FromBody] CreateDocumentRequestDto dto)
    {
        try
        {
            var result = await _documentRequestService.CreateDocumentRequestAsync(
                noticeId, dto, GetUserId());
            return CreatedAtAction(
                nameof(GetDocumentRequestById),
                new { requestId = result.Id },
                result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Document Request CRUD Endpoints
    // ==========================================================================

    /// <summary>
    /// Get a document request by ID
    /// </summary>
    [HttpGet("document-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(DocumentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentRequestById(Guid requestId)
    {
        try
        {
            var result = await _documentRequestService.GetDocumentRequestByIdAsync(
                requestId, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update a document request
    /// </summary>
    [HttpPatch("document-requests/{requestId:guid}")]
    [ProducesResponseType(typeof(DocumentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateDocumentRequest(
        Guid requestId,
        [FromBody] UpdateDocumentRequestDto dto)
    {
        try
        {
            var result = await _documentRequestService.UpdateDocumentRequestAsync(
                requestId, dto, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a document request
    /// </summary>
    [HttpDelete("document-requests/{requestId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteDocumentRequest(Guid requestId)
    {
        try
        {
            await _documentRequestService.DeleteDocumentRequestAsync(requestId, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Fulfillment Endpoints
    // ==========================================================================

    /// <summary>
    /// Upload a document to fulfill a request
    /// </summary>
    [HttpPost("document-requests/{requestId:guid}/fulfill")]
    [ProducesResponseType(typeof(DocumentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> FulfillDocumentRequest(
        Guid requestId,
        [FromForm] IFormFile file,
        [FromForm] string? note = null)
    {
        try
        {
            var result = await _documentRequestService.FulfillDocumentRequestAsync(
                requestId, file, note, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Manually mark a document request as fulfilled
    /// </summary>
    [HttpPost("document-requests/{requestId:guid}/mark-fulfilled")]
    [ProducesResponseType(typeof(DocumentRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsFulfilled(
        Guid requestId,
        [FromBody] FulfillDocumentRequestDto? dto = null)
    {
        try
        {
            var result = await _documentRequestService.MarkAsFulfilledAsync(
                requestId, GetUserId(), dto?.Note);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Review a submitted document
    /// </summary>
    [HttpPost("document-requests/{requestId:guid}/review")]
    [ProducesResponseType(typeof(DocumentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReviewDocumentRequest(
        Guid requestId,
        [FromBody] DocumentReviewDto dto)
    {
        try
        {
            var result = await _documentRequestService.ReviewDocumentRequestAsync(
                requestId, dto.Status, dto.ReviewNote, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Send a reminder for a document request
    /// </summary>
    [HttpPost("document-requests/{requestId:guid}/remind")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendReminder(Guid requestId)
    {
        try
        {
            await _documentRequestService.SendReminderAsync(requestId, GetUserId());
            return Ok(new { message = "Reminder sent" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // My Requests Endpoint
    // ==========================================================================

    /// <summary>
    /// Get current user's pending document requests
    /// </summary>
    [HttpGet("document-requests/my")]
    [ProducesResponseType(typeof(List<DocumentRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyPendingRequests()
    {
        var result = await _documentRequestService.GetMyPendingRequestsAsync(GetUserId());
        return Ok(result);
    }

    // ==========================================================================
    // Template Endpoints
    // ==========================================================================

    /// <summary>
    /// Get document request templates
    /// </summary>
    [HttpGet("document-request-templates")]
    [ProducesResponseType(typeof(List<DocumentRequestTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTemplates([FromQuery] string? noticeType = null)
    {
        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
        var result = await _documentRequestService.GetDocumentRequestTemplatesAsync(orgId, noticeType);
        return Ok(result);
    }

    /// <summary>
    /// Create a document request template
    /// </summary>
    [HttpPost("document-request-templates")]
    [ProducesResponseType(typeof(DocumentRequestTemplateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateDocumentRequestTemplateDto dto)
    {
        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
        var result = await _documentRequestService.CreateDocumentRequestTemplateAsync(dto, orgId);
        return CreatedAtAction(nameof(GetTemplates), result);
    }

    /// <summary>
    /// Delete a document request template
    /// </summary>
    [HttpDelete("document-request-templates/{templateId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTemplate(Guid templateId)
    {
        try
        {
            var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
            await _documentRequestService.DeleteDocumentRequestTemplateAsync(templateId, orgId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}

public record DocumentReviewDto(string Status, string? ReviewNote);
