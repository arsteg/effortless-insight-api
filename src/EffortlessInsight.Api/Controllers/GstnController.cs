using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.GstnIntegration;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for GSTN portal integration.
/// </summary>
[ApiController]
[Route("api/v1/gstn")]
[Authorize]
public class GstnController : ControllerBase
{
    private readonly IGstnConnectionService _connectionService;
    private readonly IGstnAuthService _authService;
    private readonly IGstnNoticeService _noticeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly ILogger<GstnController> _logger;

    public GstnController(
        IGstnConnectionService connectionService,
        IGstnAuthService authService,
        IGstnNoticeService noticeService,
        ApplicationDbContext dbContext,
        ICurrentOrganizationService currentOrg,
        ILogger<GstnController> logger)
    {
        _connectionService = connectionService;
        _authService = authService;
        _noticeService = noticeService;
        _dbContext = dbContext;
        _currentOrg = currentOrg;
        _logger = logger;
    }

    #region Connection Management

    /// <summary>
    /// Get all GSTN connections for the organization.
    /// </summary>
    [HttpGet("connections")]
    [ProducesResponseType(typeof(ApiResponse<GstnConnectionListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnections(CancellationToken cancellationToken)
    {
        var orgId = GetCurrentOrganizationId();

        var connections = await _connectionService.GetConnectionsForOrganizationAsync(orgId, cancellationToken);

        var dtos = connections.Select(c => new GstnConnectionDto(
            Id: c.Id,
            OrganizationGstinId: c.OrganizationGstinId,
            Gstin: c.OrganizationGstin.Gstin,
            TradeName: c.OrganizationGstin.TradeName,
            Status: c.Status,
            IsConnected: c.Status == GstnConnectionStatus.Connected,
            GspProvider: c.GspProvider,
            ConnectedAt: c.ConnectedAt,
            LastSyncAt: c.LastSyncAt,
            NextScheduledSyncAt: c.NextScheduledSyncAt,
            AutoSyncEnabled: c.AutoSyncEnabled,
            SyncIntervalHours: c.SyncIntervalHours,
            ConsecutiveFailures: c.ConsecutiveFailures,
            LastSyncError: c.LastSyncError,
            ConnectedByName: c.ConnectedBy?.Name
        )).ToList();

        return Ok(new ApiResponse<GstnConnectionListResponse>(true,
            new GstnConnectionListResponse(dtos, dtos.Count)));
    }

    /// <summary>
    /// Get connection status for a specific GSTIN.
    /// </summary>
    [HttpGet("connections/{gstinId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<GstnConnectionStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConnectionStatus(Guid gstinId, CancellationToken cancellationToken)
    {
        var orgId = GetCurrentOrganizationId();

        // Verify GSTIN belongs to organization
        var gstin = await _dbContext.OrganizationGstins
            .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

        if (gstin == null)
        {
            return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
        }

        var status = await _connectionService.GetConnectionStatusAsync(gstinId, cancellationToken);

        if (status == null)
        {
            return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
        }

        return Ok(new ApiResponse<GstnConnectionStatusResponse>(true,
            GstnConnectionStatusResponse.FromDto(status)));
    }

    #endregion

    #region OTP Authentication

    /// <summary>
    /// Initiate GSTN connection via OTP.
    /// </summary>
    [HttpPost("connections/{gstinId:guid}/connect")]
    [ProducesResponseType(typeof(ApiResponse<GstnConnectInitiateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InitiateConnection(Guid gstinId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission
            if (!_currentOrg.HasPermission("integrations.manage"))
            {
                return Forbid();
            }

            // Verify GSTIN belongs to organization
            var gstin = await _dbContext.OrganizationGstins
                .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

            if (gstin == null)
            {
                return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
            }

            var result = await _authService.InitiateConnectionAsync(
                gstinId,
                userId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                cancellationToken);

            return Ok(new ApiResponse<GstnConnectInitiateResponse>(true,
                new GstnConnectInitiateResponse(
                    Success: result.Success,
                    OtpDestination: result.OtpDestination,
                    OtpDestinationType: result.OtpDestinationType,
                    ExpiresAt: result.ExpiresAt,
                    ErrorCode: result.ErrorCode,
                    ErrorMessage: result.ErrorMessage
                )));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate GSTN connection for GSTIN {GstinId}", gstinId);
            return BadRequest(new ApiErrorResponse(false, "CONNECTION_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Verify OTP and complete GSTN connection.
    /// </summary>
    [HttpPost("connections/{gstinId:guid}/verify-otp")]
    [ProducesResponseType(typeof(ApiResponse<GstnVerifyOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyOtp(
        Guid gstinId,
        [FromBody] GstnVerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission
            if (!_currentOrg.HasPermission("integrations.manage"))
            {
                return Forbid();
            }

            // Verify GSTIN belongs to organization
            var gstin = await _dbContext.OrganizationGstins
                .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

            if (gstin == null)
            {
                return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
            }

            var result = await _authService.VerifyOtpAsync(gstinId, request.Otp, userId, cancellationToken);

            return Ok(new ApiResponse<GstnVerifyOtpResponse>(true,
                new GstnVerifyOtpResponse(
                    Success: result.Success,
                    Connection: result.ConnectionStatus != null
                        ? GstnConnectionStatusResponse.FromDto(result.ConnectionStatus)
                        : null,
                    ErrorCode: result.ErrorCode,
                    ErrorMessage: result.ErrorMessage,
                    RemainingAttempts: result.RemainingAttempts
                )));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify OTP for GSTIN {GstinId}", gstinId);
            return BadRequest(new ApiErrorResponse(false, "VERIFICATION_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Resend OTP for GSTN connection.
    /// This cancels any pending OTP session and initiates a new one.
    /// </summary>
    [HttpPost("connections/{gstinId:guid}/resend-otp")]
    [ProducesResponseType(typeof(ApiResponse<GstnConnectInitiateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendOtp(Guid gstinId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission
            if (!_currentOrg.HasPermission("integrations.manage"))
            {
                return Forbid();
            }

            // Verify GSTIN belongs to organization
            var gstin = await _dbContext.OrganizationGstins
                .Include(g => g.GstnConnection)
                .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

            if (gstin == null)
            {
                return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
            }

            // Check if there's a pending OTP session that's not yet expired
            // to prevent OTP flooding
            var pendingSession = await _dbContext.GstnOtpSessions
                .Where(s => s.OrganizationGstinId == gstinId)
                .Where(s => s.Status == Data.Entities.GstnOtpSessionStatus.Pending)
                .Where(s => s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (pendingSession != null)
            {
                // Allow resend if at least 1 minute has passed since last OTP
                var minResendInterval = TimeSpan.FromMinutes(1);
                if (DateTime.UtcNow - pendingSession.CreatedAt < minResendInterval)
                {
                    var waitSeconds = (int)(minResendInterval - (DateTime.UtcNow - pendingSession.CreatedAt)).TotalSeconds;
                    return StatusCode(StatusCodes.Status429TooManyRequests,
                        new ApiErrorResponse(false, "RESEND_TOO_SOON",
                            $"Please wait {waitSeconds} seconds before requesting a new OTP"));
                }
            }

            // Initiate new OTP (this cancels any existing pending session)
            var result = await _authService.InitiateConnectionAsync(
                gstinId,
                userId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                cancellationToken);

            return Ok(new ApiResponse<GstnConnectInitiateResponse>(true,
                new GstnConnectInitiateResponse(
                    Success: result.Success,
                    OtpDestination: result.OtpDestination,
                    OtpDestinationType: result.OtpDestinationType,
                    ExpiresAt: result.ExpiresAt,
                    ErrorCode: result.ErrorCode,
                    ErrorMessage: result.ErrorMessage
                )));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend OTP for GSTIN {GstinId}", gstinId);
            return BadRequest(new ApiErrorResponse(false, "RESEND_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Disconnect from GSTN portal.
    /// </summary>
    [HttpPost("connections/{gstinId:guid}/disconnect")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Disconnect(
        Guid gstinId,
        [FromBody] GstnDisconnectRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission
            if (!_currentOrg.HasPermission("integrations.manage"))
            {
                return Forbid();
            }

            // Verify GSTIN belongs to organization
            var gstin = await _dbContext.OrganizationGstins
                .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

            if (gstin == null)
            {
                return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
            }

            await _connectionService.DisconnectAsync(gstinId, userId, request?.Reason, cancellationToken);

            return Ok(new ApiResponse<object>(true, new { message = "Disconnected successfully" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect GSTIN {GstinId}", gstinId);
            return BadRequest(new ApiErrorResponse(false, "DISCONNECT_ERROR", ex.Message));
        }
    }

    #endregion

    #region Sync Operations

    /// <summary>
    /// Trigger manual sync for a GSTIN connection.
    /// </summary>
    [HttpPost("connections/{gstinId:guid}/sync")]
    [ProducesResponseType(typeof(ApiResponse<GstnSyncTriggerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerSync(Guid gstinId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission
            if (!_currentOrg.HasPermission("integrations.manage"))
            {
                return Forbid();
            }

            // Verify GSTIN belongs to organization
            var gstin = await _dbContext.OrganizationGstins
                .Include(g => g.GstnConnection)
                .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

            if (gstin == null)
            {
                return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
            }

            if (gstin.GstnConnection == null || gstin.GstnConnection.Status != GstnConnectionStatus.Connected)
            {
                return BadRequest(new ApiErrorResponse(false, "NOT_CONNECTED",
                    "GSTIN is not connected to the GST portal"));
            }

            var result = await _noticeService.SyncNoticesAsync(
                gstin.GstnConnection.Id,
                new GstnSyncOptions
                {
                    SyncType = GstnSyncType.Incremental,
                    TriggerSource = GstnSyncTrigger.Manual,
                    TriggeredById = userId
                },
                cancellationToken);

            if (result.Success)
            {
                await _connectionService.RecordSyncSuccessAsync(gstin.GstnConnection.Id, cancellationToken);
            }
            else
            {
                await _connectionService.RecordSyncFailureAsync(
                    gstin.GstnConnection.Id,
                    result.ErrorMessage ?? "Unknown error",
                    cancellationToken);
            }

            return Ok(new ApiResponse<GstnSyncTriggerResponse>(true,
                new GstnSyncTriggerResponse(
                    Success: result.Success,
                    SyncLogId: result.SyncLogId,
                    NoticesFound: result.NoticesFound,
                    NoticesImported: result.NoticesImported,
                    NoticesSkipped: result.NoticesSkipped,
                    NoticesFailed: result.NoticesFailed,
                    ErrorCode: result.ErrorCode,
                    ErrorMessage: result.ErrorMessage
                )));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger sync for GSTIN {GstinId}", gstinId);
            return BadRequest(new ApiErrorResponse(false, "SYNC_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Get sync history for a GSTIN connection.
    /// </summary>
    [HttpGet("connections/{gstinId:guid}/sync-logs")]
    [ProducesResponseType(typeof(ApiResponse<GstnSyncHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSyncLogs(
        Guid gstinId,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var orgId = GetCurrentOrganizationId();

        // Verify GSTIN belongs to organization
        var gstin = await _dbContext.OrganizationGstins
            .Include(g => g.GstnConnection)
            .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

        if (gstin == null)
        {
            return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
        }

        if (gstin.GstnConnection == null)
        {
            return Ok(new ApiResponse<GstnSyncHistoryResponse>(true,
                new GstnSyncHistoryResponse([], 0)));
        }

        var logs = await _noticeService.GetSyncLogsAsync(gstin.GstnConnection.Id, limit, cancellationToken);

        var dtos = logs.Select(l => new GstnSyncLogEntryDto(
            Id: l.Id,
            SyncType: l.SyncType,
            Status: l.Status,
            TriggerSource: l.TriggerSource,
            StartedAt: l.StartedAt,
            CompletedAt: l.CompletedAt,
            DurationMs: l.DurationMs,
            NoticesFound: l.NoticesFound,
            NoticesImported: l.NoticesImported,
            NoticesSkipped: l.NoticesSkipped,
            NoticesFailed: l.NoticesFailed,
            ErrorMessage: l.ErrorMessage,
            TriggeredByName: l.TriggeredByName
        )).ToList();

        return Ok(new ApiResponse<GstnSyncHistoryResponse>(true,
            new GstnSyncHistoryResponse(dtos, dtos.Count)));
    }

    #endregion

    #region Settings

    /// <summary>
    /// Update connection settings.
    /// </summary>
    [HttpPatch("connections/{gstinId:guid}/settings")]
    [ProducesResponseType(typeof(ApiResponse<GstnSettingsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSettings(
        Guid gstinId,
        [FromBody] GstnUpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission
            if (!_currentOrg.HasPermission("integrations.manage"))
            {
                return Forbid();
            }

            // Verify GSTIN belongs to organization
            var gstin = await _dbContext.OrganizationGstins
                .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == orgId, cancellationToken);

            if (gstin == null)
            {
                return NotFound(new ApiErrorResponse(false, "GSTIN_NOT_FOUND", "GSTIN not found"));
            }

            var connection = await _connectionService.UpdateSettingsAsync(
                gstinId,
                new UpdateGstnConnectionSettingsRequest(
                    AutoSyncEnabled: request.AutoSyncEnabled,
                    SyncIntervalHours: request.SyncIntervalHours
                ),
                userId,
                cancellationToken);

            return Ok(new ApiResponse<GstnSettingsResponse>(true,
                new GstnSettingsResponse(
                    AutoSyncEnabled: connection.AutoSyncEnabled,
                    SyncIntervalHours: connection.SyncIntervalHours,
                    NextScheduledSyncAt: connection.NextScheduledSyncAt
                )));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ApiErrorResponse(false, "CONNECTION_NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update settings for GSTIN {GstinId}", gstinId);
            return BadRequest(new ApiErrorResponse(false, "UPDATE_ERROR", ex.Message));
        }
    }

    #endregion

    #region Helper Methods

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        return userId;
    }

    private Guid GetCurrentOrganizationId()
    {
        var orgId = _currentOrg.OrganizationId;
        if (!orgId.HasValue)
        {
            throw new InvalidOperationException("No organization context");
        }
        return orgId.Value;
    }

    #endregion
}
