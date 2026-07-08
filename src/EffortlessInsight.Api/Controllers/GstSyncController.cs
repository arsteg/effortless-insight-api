using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.GstSync;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for GST Sync module - Chrome Extension and Desktop Agent integration.
/// Handles GSTIN monitoring, notice sync, and extension management.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/gst-sync")]
public class GstSyncController : ControllerBase
{
    private readonly IGstClientService _clientService;
    private readonly IGstSyncService _syncService;
    private readonly IGstNoticeRawService _noticeService;
    private readonly IGstExtensionService _extensionService;
    private readonly ICurrentOrganizationService _currentOrgService;
    private readonly ILogger<GstSyncController> _logger;

    public GstSyncController(
        IGstClientService clientService,
        IGstSyncService syncService,
        IGstNoticeRawService noticeService,
        IGstExtensionService extensionService,
        ICurrentOrganizationService currentOrgService,
        ILogger<GstSyncController> logger)
    {
        _clientService = clientService;
        _syncService = syncService;
        _noticeService = noticeService;
        _extensionService = extensionService;
        _currentOrgService = currentOrgService;
        _logger = logger;
    }

    private Guid GetOrganizationId()
    {
        var orgId = _currentOrgService.OrganizationId;
        if (!orgId.HasValue)
            throw new UnauthorizedAccessException("Organization context required");
        return orgId.Value;
    }

    private Guid GetUserId()
    {
        var userId = _currentOrgService.UserId;
        if (!userId.HasValue)
            throw new UnauthorizedAccessException("User context required");
        return userId.Value;
    }

    // ============================================================================
    // GST CLIENT ENDPOINTS
    // ============================================================================

    /// <summary>
    /// Get all GST clients for the current organization.
    /// </summary>
    [HttpGet("clients")]
    [ProducesResponseType(typeof(ApiResponse<GstClientListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClients(CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var clients = await _clientService.GetClientsAsync(orgId, cancellationToken);
        return Ok(new ApiResponse<GstClientListResponse>(true, new GstClientListResponse(clients, clients.Count)));
    }

    /// <summary>
    /// Get a specific GST client by ID.
    /// </summary>
    [HttpGet("clients/{clientId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GstClientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClient(Guid clientId, CancellationToken cancellationToken)
    {
        var client = await _clientService.GetClientByIdAsync(clientId, cancellationToken);
        if (client == null)
            return NotFound(new ApiErrorResponse(false, "CLIENT_NOT_FOUND", "GST client not found"));
        return Ok(new ApiResponse<GstClientDto>(true, client));
    }

    /// <summary>
    /// Create a new GST client connection.
    /// </summary>
    [HttpPost("clients")]
    [ProducesResponseType(typeof(ApiResponse<GstClientDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateClient([FromBody] CreateGstClientRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetOrganizationId();
            var userId = GetUserId();
            var client = await _clientService.CreateClientAsync(orgId, userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetClient), new { clientId = client.Id }, new ApiResponse<GstClientDto>(true, client));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "DUPLICATE_GSTIN", ex.Message));
        }
    }

    /// <summary>
    /// Update a GST client connection.
    /// </summary>
    [HttpPatch("clients/{clientId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GstClientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateClient(Guid clientId, [FromBody] UpdateGstClientRequest request, CancellationToken cancellationToken)
    {
        var client = await _clientService.UpdateClientAsync(clientId, request, cancellationToken);
        if (client == null)
            return NotFound(new ApiErrorResponse(false, "CLIENT_NOT_FOUND", "GST client not found"));
        return Ok(new ApiResponse<GstClientDto>(true, client));
    }

    /// <summary>
    /// Delete a GST client connection.
    /// </summary>
    [HttpDelete("clients/{clientId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteClient(Guid clientId, CancellationToken cancellationToken)
    {
        var deleted = await _clientService.DeleteClientAsync(clientId, cancellationToken);
        if (!deleted)
            return NotFound(new ApiErrorResponse(false, "CLIENT_NOT_FOUND", "GST client not found"));
        return NoContent();
    }

    /// <summary>
    /// Pause sync for a GST client.
    /// </summary>
    [HttpPost("clients/{clientId:guid}/pause")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PauseSync(Guid clientId, CancellationToken cancellationToken)
    {
        var paused = await _clientService.PauseSyncAsync(clientId, cancellationToken);
        if (!paused)
            return NotFound(new ApiErrorResponse(false, "CLIENT_NOT_FOUND", "GST client not found"));
        return Ok(new ApiResponse<object>(true, new { message = "Sync paused" }));
    }

    /// <summary>
    /// Resume sync for a GST client.
    /// </summary>
    [HttpPost("clients/{clientId:guid}/resume")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResumeSync(Guid clientId, CancellationToken cancellationToken)
    {
        var resumed = await _clientService.ResumeSyncAsync(clientId, cancellationToken);
        if (!resumed)
            return NotFound(new ApiErrorResponse(false, "CLIENT_NOT_FOUND", "GST client not found"));
        return Ok(new ApiResponse<object>(true, new { message = "Sync resumed" }));
    }

    // ============================================================================
    // SYNC SESSION ENDPOINTS
    // ============================================================================

    /// <summary>
    /// Start a new sync session.
    /// </summary>
    [HttpPost("sync/start")]
    [ProducesResponseType(typeof(ApiResponse<GstSyncSessionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartSyncSession([FromBody] StartSyncSessionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetOrganizationId();
            var session = await _syncService.StartSessionAsync(orgId, request, cancellationToken);
            return CreatedAtAction(nameof(GetSyncSession), new { sessionId = session.Id }, new ApiResponse<GstSyncSessionDto>(true, session));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "SYNC_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Sync notices (batch upload from extension/agent).
    /// </summary>
    [HttpPost("sync/notices")]
    [ProducesResponseType(typeof(ApiResponse<SyncNoticesResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SyncNotices([FromBody] SyncNoticesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetOrganizationId();
            var result = await _syncService.SyncNoticesAsync(orgId, request, cancellationToken);
            return Ok(new ApiResponse<SyncNoticesResult>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "SYNC_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Complete a sync session.
    /// </summary>
    [HttpPost("sync/complete")]
    [ProducesResponseType(typeof(ApiResponse<GstSyncSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteSyncSession([FromBody] CompleteSyncSessionRequest request, CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var session = await _syncService.CompleteSessionAsync(orgId, request, cancellationToken);
        if (session == null)
            return NotFound(new ApiErrorResponse(false, "SESSION_NOT_FOUND", "Sync session not found"));
        return Ok(new ApiResponse<GstSyncSessionDto>(true, session));
    }

    /// <summary>
    /// Get a specific sync session.
    /// </summary>
    [HttpGet("sync/{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GstSyncSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSyncSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await _syncService.GetSessionByIdAsync(sessionId, cancellationToken);
        if (session == null)
            return NotFound(new ApiErrorResponse(false, "SESSION_NOT_FOUND", "Sync session not found"));
        return Ok(new ApiResponse<GstSyncSessionDto>(true, session));
    }

    /// <summary>
    /// Get all sync sessions for the organization.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ApiResponse<GstSyncSessionListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions(
        [FromQuery] Guid? gstClientId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var orgId = GetOrganizationId();
        var sessions = await _syncService.GetSessionsAsync(orgId, gstClientId, status, page, pageSize, cancellationToken);
        return Ok(new ApiResponse<GstSyncSessionListResponse>(true, sessions));
    }

    /// <summary>
    /// Get sync history for a GST client.
    /// </summary>
    [HttpGet("clients/{clientId:guid}/sync-history")]
    [ProducesResponseType(typeof(ApiResponse<List<GstSyncSessionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSyncHistory(Guid clientId, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var history = await _syncService.GetSyncHistoryAsync(clientId, limit, cancellationToken);
        return Ok(new ApiResponse<List<GstSyncSessionDto>>(true, history));
    }

    // ============================================================================
    // STATISTICS ENDPOINTS
    // ============================================================================

    /// <summary>
    /// Get GST sync statistics for the organization.
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(ApiResponse<GstSyncStatisticsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var stats = await _syncService.GetStatisticsAsync(orgId, cancellationToken);
        return Ok(new ApiResponse<GstSyncStatisticsDto>(true, stats));
    }

    // ============================================================================
    // RAW NOTICE ENDPOINTS
    // ============================================================================

    /// <summary>
    /// Get raw notices for a GST client.
    /// </summary>
    [HttpGet("clients/{clientId:guid}/notices")]
    [ProducesResponseType(typeof(ApiResponse<GstNoticeRawListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClientNotices(
        Guid clientId,
        [FromQuery] bool? imported = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var notices = await _noticeService.GetNoticesAsync(clientId, imported, cancellationToken);
        var totalCount = notices.Count;
        var pagedNotices = notices.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new ApiResponse<GstNoticeRawListResponse>(true, new GstNoticeRawListResponse
        {
            Items = pagedNotices,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        }));
    }

    /// <summary>
    /// Get all raw notices for the organization.
    /// </summary>
    [HttpGet("notices")]
    [ProducesResponseType(typeof(ApiResponse<GstNoticeRawListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllNotices(
        [FromQuery] bool? imported = null,
        [FromQuery] string? importStatus = null,
        [FromQuery] string? gstClientId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Handle importStatus parameter (pending = false, imported = true, all = null)
        bool? importedFilter = imported;
        if (!imported.HasValue && !string.IsNullOrEmpty(importStatus))
        {
            importedFilter = importStatus.ToLowerInvariant() switch
            {
                "pending" => false,
                "imported" => true,
                _ => null // "all" or any other value
            };
        }

        var orgId = GetOrganizationId();
        var notices = await _noticeService.GetNoticesByOrganizationAsync(orgId, importedFilter, cancellationToken);

        // Filter by GST client if specified
        if (!string.IsNullOrEmpty(gstClientId) && Guid.TryParse(gstClientId, out var clientGuid))
        {
            notices = notices.Where(n => n.GstClientId == clientGuid).ToList();
        }

        var totalCount = notices.Count;
        var pagedNotices = notices.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new ApiResponse<GstNoticeRawListResponse>(true, new GstNoticeRawListResponse
        {
            Items = pagedNotices,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        }));
    }

    /// <summary>
    /// Get a specific raw notice.
    /// </summary>
    [HttpGet("notices/{noticeId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GstNoticeRawDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotice(Guid noticeId, CancellationToken cancellationToken)
    {
        var notice = await _noticeService.GetNoticeByIdAsync(noticeId, cancellationToken);
        if (notice == null)
            return NotFound(new ApiErrorResponse(false, "NOTICE_NOT_FOUND", "Notice not found"));
        return Ok(new ApiResponse<GstNoticeRawDto>(true, notice));
    }

    /// <summary>
    /// Get upcoming due dates for notifications.
    /// Returns notices with due dates within the next 14 days or overdue.
    /// </summary>
    [HttpGet("notices/upcoming-due-dates")]
    [ProducesResponseType(typeof(ApiResponse<UpcomingDueDatesResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUpcomingDueDates(CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var notices = await _noticeService.GetUpcomingDueDatesAsync(orgId, cancellationToken);
        return Ok(new ApiResponse<UpcomingDueDatesResponse>(true, notices));
    }

    /// <summary>
    /// Import raw notices to the main Notices module.
    /// </summary>
    [HttpPost("notices/import")]
    [ProducesResponseType(typeof(ApiResponse<ImportNoticesResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportNotices([FromBody] ImportNoticesRequest request, CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var userId = GetUserId();
        var result = await _noticeService.ImportNoticesAsync(orgId, userId, request, cancellationToken);
        return Ok(new ApiResponse<ImportNoticesResult>(true, result));
    }

    /// <summary>
    /// Get presigned URL for PDF upload.
    /// </summary>
    [HttpPost("notices/pdf/upload-url")]
    [ProducesResponseType(typeof(ApiResponse<PdfUploadUrlResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPdfUploadUrl([FromBody] GetPdfUploadUrlRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetOrganizationId();
            var result = await _noticeService.GetPdfUploadUrlAsync(orgId, request, cancellationToken);
            return Ok(new ApiResponse<PdfUploadUrlResponse>(true, result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "UPLOAD_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Confirm PDF upload completed.
    /// </summary>
    [HttpPost("notices/pdf/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmPdfUpload([FromBody] ConfirmPdfUploadRequest request, CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var confirmed = await _noticeService.ConfirmPdfUploadAsync(orgId, request, cancellationToken);
        if (!confirmed)
            return NotFound(new ApiErrorResponse(false, "NOTICE_NOT_FOUND", "Notice not found"));
        return Ok(new ApiResponse<object>(true, new { message = "PDF upload confirmed" }));
    }

    // ============================================================================
    // EXTENSION ENDPOINTS
    // ============================================================================

    /// <summary>
    /// Get extension configuration.
    /// </summary>
    [HttpGet("extension/config")]
    [ProducesResponseType(typeof(ApiResponse<ExtensionConfigResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExtensionConfig(CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var config = await _extensionService.GetConfigAsync(orgId, cancellationToken);
        return Ok(new ApiResponse<ExtensionConfigResponse>(true, config));
    }

    /// <summary>
    /// Extension heartbeat.
    /// </summary>
    [HttpPost("extension/heartbeat")]
    [ProducesResponseType(typeof(ApiResponse<ExtensionHeartbeatResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExtensionHeartbeat([FromBody] ExtensionHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var orgId = GetOrganizationId();
        var userId = GetUserId();
        var response = await _extensionService.HeartbeatAsync(orgId, userId, request, cancellationToken);
        return Ok(new ApiResponse<ExtensionHeartbeatResponse>(true, response));
    }

    /// <summary>
    /// Log extension event.
    /// </summary>
    [HttpPost("extension/event")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LogExtensionEvent([FromBody] LogExtensionEventRequest request, CancellationToken cancellationToken)
    {
        var orgId = _currentOrgService.OrganizationId;
        var userId = _currentOrgService.UserId;
        await _extensionService.LogEventAsync(orgId, userId, request, cancellationToken);
        return Ok(new ApiResponse<object>(true, new { message = "Event logged" }));
    }
}
