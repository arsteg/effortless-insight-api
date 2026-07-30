using System.Security.Claims;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Organization data export endpoints: request an export, poll its status,
/// list past exports and fetch a download link. Access control (member /
/// admin checks, rate limits) is enforced by <see cref="IDataExportService"/>.
/// </summary>
[ApiController]
[Route("api/v1/organizations/{orgId:guid}/exports")]
[Authorize]
public class DataExportsController : ControllerBase
{
    private readonly IDataExportService _exportService;
    private readonly ILogger<DataExportsController> _logger;

    public DataExportsController(
        IDataExportService exportService,
        ILogger<DataExportsController> logger)
    {
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>
    /// Request a new data export for the organization (owner/admin only).
    /// The export is produced in the background; an email is sent when ready.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<DataExportResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestExport(Guid orgId, [FromBody] DataExportRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _exportService.RequestExportAsync(orgId, request, GetCurrentUserId(), ct);
            return Accepted(new ApiResponse<DataExportResult>(true, result));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, ex.Message, "You do not have permission to export this organization's data"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "EXPORT_RATE_LIMIT_EXCEEDED")
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ApiErrorResponse(false, "EXPORT_RATE_LIMIT_EXCEEDED", "Daily export limit reached. Try again tomorrow."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request export for organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// List the organization's exports, newest first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<DataExportStatus>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListExports(Guid orgId, CancellationToken ct)
    {
        try
        {
            var result = await _exportService.ListExportsAsync(orgId, GetCurrentUserId(), ct);
            return Ok(new ApiResponse<List<DataExportStatus>>(true, result));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, ex.Message, "You are not a member of this organization"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list exports for organization {OrgId}", orgId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get the status of a single export.
    /// </summary>
    [HttpGet("{exportId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DataExportStatus>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExportStatus(Guid orgId, Guid exportId, CancellationToken ct)
    {
        try
        {
            var result = await _exportService.GetExportStatusAsync(exportId, GetCurrentUserId(), ct);
            return Ok(new ApiResponse<DataExportStatus>(true, result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "EXPORT_NOT_FOUND", "Export not found"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, ex.Message, "You are not a member of this organization"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get export {ExportId}", exportId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get a short-lived pre-signed download URL for a completed export.
    /// </summary>
    [HttpGet("{exportId:guid}/download")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDownloadUrl(Guid orgId, Guid exportId, CancellationToken ct)
    {
        try
        {
            var url = await _exportService.GetDownloadUrlAsync(exportId, GetCurrentUserId(), ct);
            return Ok(new ApiResponse<string>(true, url));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "EXPORT_NOT_FOUND", "Export not found"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, ex.Message, "You are not a member of this organization"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, ex.Message, ex.Message switch
            {
                "EXPORT_NOT_READY" => "The export has not finished yet",
                "EXPORT_EXPIRED" => "The export file has expired",
                "EXPORT_FILE_MISSING" => "The export file is no longer available",
                _ => "The export cannot be downloaded"
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get download URL for export {ExportId}", exportId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        return Guid.Parse(userIdClaim);
    }
}
