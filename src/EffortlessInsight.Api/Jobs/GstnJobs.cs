using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.GstnIntegration;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background jobs for GSTN portal integration.
/// </summary>
public class GstnJobs
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGstnAuthService _authService;
    private readonly IGstnNoticeService _noticeService;
    private readonly IGstnConnectionService _connectionService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly GstnOptions _options;
    private readonly ILogger<GstnJobs> _logger;

    public GstnJobs(
        ApplicationDbContext dbContext,
        IGstnAuthService authService,
        IGstnNoticeService noticeService,
        IGstnConnectionService connectionService,
        IBackgroundJobClient backgroundJobs,
        IOptions<GstnOptions> options,
        ILogger<GstnJobs> logger)
    {
        _dbContext = dbContext;
        _authService = authService;
        _noticeService = noticeService;
        _connectionService = connectionService;
        _backgroundJobs = backgroundJobs;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes tokens that are about to expire.
    /// Should be scheduled to run every hour.
    /// </summary>
    public async Task RefreshExpiringTokensAsync()
    {
        if (!_options.Enabled) return;

        try
        {
            var connections = await _connectionService.GetConnectionsNeedingRefreshAsync();

            _logger.LogInformation(
                "Found {Count} connections needing token refresh",
                connections.Count);

            var refreshed = 0;
            var failed = 0;

            foreach (var connection in connections)
            {
                try
                {
                    var result = await _authService.RefreshTokenAsync(connection.Id);

                    if (result.Success)
                    {
                        refreshed++;
                        _logger.LogDebug(
                            "Refreshed token for connection {ConnectionId}",
                            connection.Id);
                    }
                    else
                    {
                        failed++;
                        _logger.LogWarning(
                            "Failed to refresh token for connection {ConnectionId}: {Error}",
                            connection.Id,
                            result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "Error refreshing token for connection {ConnectionId}",
                        connection.Id);
                }
            }

            _logger.LogInformation(
                "Token refresh job completed: {Refreshed} refreshed, {Failed} failed",
                refreshed,
                failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh job failed");
            throw;
        }
    }

    /// <summary>
    /// Processes scheduled notice syncs.
    /// Should be scheduled to run every 15 minutes.
    /// </summary>
    public async Task ProcessScheduledSyncsAsync()
    {
        if (!_options.Enabled) return;

        try
        {
            var connections = await _connectionService.GetConnectionsDueForSyncAsync();

            _logger.LogInformation(
                "Found {Count} connections due for sync",
                connections.Count);

            foreach (var connection in connections)
            {
                // Enqueue individual sync jobs to process in parallel
                _backgroundJobs.Enqueue<GstnJobs>(
                    job => job.SyncConnectionAsync(connection.Id));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process scheduled syncs");
            throw;
        }
    }

    /// <summary>
    /// Syncs notices for a specific connection.
    /// Called by ProcessScheduledSyncsAsync or manually.
    /// </summary>
    public async Task SyncConnectionAsync(Guid connectionId)
    {
        if (!_options.Enabled) return;

        try
        {
            _logger.LogInformation("Starting sync for connection {ConnectionId}", connectionId);

            var result = await _noticeService.SyncNoticesAsync(
                connectionId,
                new GstnSyncOptions
                {
                    SyncType = GstnSyncType.Incremental,
                    TriggerSource = GstnSyncTrigger.Scheduled
                });

            if (result.Success)
            {
                await _connectionService.RecordSyncSuccessAsync(connectionId);

                _logger.LogInformation(
                    "Sync completed for connection {ConnectionId}: {Imported} imported, {Skipped} skipped",
                    connectionId,
                    result.NoticesImported,
                    result.NoticesSkipped);
            }
            else
            {
                await _connectionService.RecordSyncFailureAsync(
                    connectionId,
                    result.ErrorMessage ?? "Unknown error");

                _logger.LogWarning(
                    "Sync failed for connection {ConnectionId}: {Error}",
                    connectionId,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            await _connectionService.RecordSyncFailureAsync(connectionId, ex.Message);
            _logger.LogError(ex, "Error during sync for connection {ConnectionId}", connectionId);
            throw;
        }
    }

    /// <summary>
    /// Downloads a notice document from the GSTN portal.
    /// Called on-demand after notice import.
    /// </summary>
    public async Task DownloadNoticeDocumentAsync(Guid noticeId)
    {
        if (!_options.Enabled) return;

        try
        {
            _logger.LogInformation("Downloading document for notice {NoticeId}", noticeId);

            var result = await _noticeService.DownloadNoticeDocumentAsync(noticeId);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Downloaded document for notice {NoticeId}: {FileName}",
                    noticeId,
                    result.FileName);

                // Queue AI processing if needed
                var notice = await _dbContext.Notices.FindAsync(noticeId);
                if (notice != null && notice.ProcessingStatus == NoticeProcessingStatus.Queued)
                {
                    _backgroundJobs.Enqueue<NoticeProcessingJob>(
                        job => job.ProcessAsync(noticeId, CancellationToken.None));
                }
            }
            else
            {
                _logger.LogWarning(
                    "Failed to download document for notice {NoticeId}: {Error}",
                    noticeId,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading document for notice {NoticeId}", noticeId);
            throw;
        }
    }

    /// <summary>
    /// Cleans up expired OTP sessions and old sync logs.
    /// Should be scheduled to run daily.
    /// </summary>
    public async Task CleanupAsync()
    {
        if (!_options.Enabled) return;

        try
        {
            // Clean up expired OTP sessions
            var otpsCleaned = await _authService.CleanupExpiredOtpSessionsAsync();

            // Mark stuck in-progress sync logs as failed
            // A sync should complete within 1 hour, anything longer is considered stuck
            var stuckThreshold = DateTime.UtcNow.AddHours(-1);
            var stuckSyncLogs = await _dbContext.GstnSyncLogs
                .Where(l => l.Status == GstnSyncStatus.InProgress)
                .Where(l => l.StartedAt < stuckThreshold)
                .ToListAsync();

            foreach (var stuckLog in stuckSyncLogs)
            {
                stuckLog.Status = GstnSyncStatus.Failed;
                stuckLog.ErrorMessage = "Sync job timed out or crashed - marked as failed by cleanup job";
                stuckLog.CompletedAt = DateTime.UtcNow;
                stuckLog.DurationMs = (long)(DateTime.UtcNow - stuckLog.StartedAt).TotalMilliseconds;
            }

            if (stuckSyncLogs.Count > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogWarning(
                    "Marked {Count} stuck in-progress sync logs as failed",
                    stuckSyncLogs.Count);
            }

            // Clean up old sync logs
            var syncLogsCutoff = DateTime.UtcNow.AddDays(-_options.SyncLogRetentionDays);
            var oldSyncLogs = await _dbContext.GstnSyncLogs
                .Where(l => l.CreatedAt < syncLogsCutoff)
                .ToListAsync();

            if (oldSyncLogs.Count > 0)
            {
                _dbContext.GstnSyncLogs.RemoveRange(oldSyncLogs);
                await _dbContext.SaveChangesAsync();
            }

            // Try to unsuspend eligible connections
            var unsuspended = await _connectionService.UnsuspendEligibleConnectionsAsync();

            _logger.LogInformation(
                "GSTN cleanup completed: {OtpsCleaned} OTP sessions, {StuckLogs} stuck logs fixed, {SyncLogsCleaned} old sync logs cleaned, {Unsuspended} connections unsuspended",
                otpsCleaned,
                stuckSyncLogs.Count,
                oldSyncLogs.Count,
                unsuspended);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GSTN cleanup job failed");
            throw;
        }
    }

    /// <summary>
    /// Validates all active connections and marks invalid ones.
    /// Should be scheduled to run every 6 hours.
    /// </summary>
    public async Task ValidateConnectionsAsync()
    {
        if (!_options.Enabled) return;

        try
        {
            var activeConnections = await _dbContext.GstnConnections
                .Where(c => c.Status == GstnConnectionStatus.Connected)
                .ToListAsync();

            _logger.LogInformation(
                "Validating {Count} active connections",
                activeConnections.Count);

            var validated = 0;
            var expired = 0;

            foreach (var connection in activeConnections)
            {
                try
                {
                    var isValid = await _authService.ValidateTokenAsync(connection.Id);

                    if (isValid)
                    {
                        validated++;
                    }
                    else
                    {
                        connection.Status = GstnConnectionStatus.TokenExpired;
                        connection.UpdatedAt = DateTime.UtcNow;
                        expired++;

                        _logger.LogWarning(
                            "Connection {ConnectionId} token is no longer valid",
                            connection.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to validate connection {ConnectionId}",
                        connection.Id);
                }
            }

            if (expired > 0)
            {
                await _dbContext.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Connection validation completed: {Validated} valid, {Expired} expired",
                validated,
                expired);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection validation job failed");
            throw;
        }
    }
}
