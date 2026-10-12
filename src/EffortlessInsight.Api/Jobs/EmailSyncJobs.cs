using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.EmailSync;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background jobs for email sync integration.
/// </summary>
public class EmailSyncJobs
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailSyncService _emailSyncService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly EmailSyncOptions _options;
    private readonly ILogger<EmailSyncJobs> _logger;

    public EmailSyncJobs(
        ApplicationDbContext dbContext,
        IEmailSyncService emailSyncService,
        IBackgroundJobClient backgroundJobs,
        IOptions<EmailSyncOptions> options,
        ILogger<EmailSyncJobs> logger)
    {
        _dbContext = dbContext;
        _emailSyncService = emailSyncService;
        _backgroundJobs = backgroundJobs;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Processes daily email sync for all connected accounts.
    /// Scheduled once daily in the configured time zone.
    /// </summary>
    public async Task ProcessDailySyncAsync()
    {
        if (!_options.Enabled) return;

        try
        {
            var accounts = await _emailSyncService.GetAccountsDueForSyncAsync();

            _logger.LogInformation(
                "Found {Count} email accounts due for sync",
                accounts.Count);

            foreach (var account in accounts)
            {
                // Enqueue individual sync jobs to process in parallel
                _backgroundJobs.Enqueue<EmailSyncJobs>(
                    job => job.SyncAccountAsync(account.Id));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process daily email sync");
            throw;
        }
    }

    /// <summary>
    /// Syncs emails for a specific account.
    /// Called by ProcessDailySyncAsync or manually.
    /// </summary>
    public async Task SyncAccountAsync(Guid accountId)
    {
        if (!_options.Enabled) return;

        try
        {
            _logger.LogInformation("Starting email sync for account {AccountId}", accountId);

            var result = await _emailSyncService.SyncAccountAsync(
                accountId,
                triggeredById: null,
                triggerSource: EmailSyncTrigger.Scheduled);

            if (result.ErrorCode == "sync_in_progress")
            {
                // No scan/day claim occurred. Retry coordination only; the service enforces one scan/day.
                _backgroundJobs.Schedule<EmailSyncJobs>(job => job.SyncAccountAsync(accountId), TimeSpan.FromMinutes(5));
                return;
            }
            if (result.Success)
            {
                _logger.LogInformation(
                    "Email sync completed for account {AccountId}: {Imported} notices imported, {Skipped} duplicates skipped",
                    accountId,
                    result.NoticesImported,
                    result.DuplicatesSkipped);
            }
            else
            {
                _logger.LogWarning(
                    "Email sync failed for account {AccountId}: {Error}",
                    accountId,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing email account {AccountId}", accountId);
            throw;
        }
    }

    /// <summary>
    /// Refreshes tokens that are about to expire.
    /// Scheduled every five minutes; does not fetch messages.
    /// </summary>
    public async Task RefreshExpiringTokensAsync()
    {
        if (!_options.Enabled) return;

        try
        {
            var accounts = await _emailSyncService.GetAccountsNeedingRefreshAsync();

            _logger.LogInformation(
                "Found {Count} email accounts needing token refresh",
                accounts.Count);

            var refreshed = 0;
            var failed = 0;

            foreach (var account in accounts)
            {
                try
                {
                    var success = await _emailSyncService.RefreshTokenIfNeededAsync(account.Id);

                    if (success)
                    {
                        refreshed++;
                        _logger.LogDebug(
                            "Refreshed token for email account {AccountId}",
                            account.Id);
                    }
                    else
                    {
                        failed++;
                        _logger.LogWarning(
                            "Failed to refresh token for email account {AccountId}",
                            account.Id);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "Error refreshing token for email account {AccountId}",
                        account.Id);
                }
            }

            _logger.LogInformation(
                "Email token refresh job completed: {Refreshed} refreshed, {Failed} failed",
                refreshed,
                failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email token refresh job failed");
            throw;
        }
    }

    /// <summary>
    /// Cleans up old sync logs and unsuspends eligible accounts.
    /// Should be scheduled to run daily at 3 AM UTC.
    /// </summary>
    public async Task CleanupAsync()
    {
        if (!_options.Enabled) return;

        try
        {
            // Clean up old sync logs
            var retentionCutoff = DateTime.UtcNow.AddDays(-_options.SyncLogRetentionDays);

            var deletedLogs = await _dbContext.EmailSyncLogs
                .Where(l => l.CreatedAt < retentionCutoff)
                .ExecuteDeleteAsync();

            _logger.LogInformation(
                "Deleted {Count} old email sync logs",
                deletedLogs);

            // Recover an interruption between saving an email notice and enqueueing its job.
            var queueCutoff = DateTime.UtcNow.AddMinutes(-15);
            var queuedIds = await _dbContext.Notices.IgnoreQueryFilters().Where(n => n.DeletedAt == null &&
                n.Source == NoticeSource.Email && n.ProcessingStatus == "queued" && n.CreatedAt < queueCutoff)
                .OrderBy(n => n.CreatedAt).Take(100).Select(n => n.Id).ToListAsync();
            foreach (var id in queuedIds)
                _backgroundJobs.Enqueue<EffortlessInsight.Api.Services.Notices.INoticeProcessingJob>(j => j.ProcessAsync(id, CancellationToken.None));
            var staleLogs = await _dbContext.EmailSyncLogs.Where(l => l.Status == EmailSyncStatus.InProgress && l.StartedAt < DateTime.UtcNow.AddHours(-2)).ToListAsync();
            foreach (var log in staleLogs)
            { log.Status = EmailSyncStatus.Failed; log.CompletedAt = DateTime.UtcNow; log.ErrorMessage = "Sync was interrupted. Pending notices will be retried."; }
            await _dbContext.SaveChangesAsync();

            // Unsuspend eligible accounts
            var unsuspended = await _emailSyncService.UnsuspendEligibleAccountsAsync();

            if (unsuspended > 0)
            {
                _logger.LogInformation(
                    "Unsuspended {Count} email accounts",
                    unsuspended);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email sync cleanup job failed");
            throw;
        }
    }

    /// <summary>
    /// Initial sync for a newly connected account.
    /// Enqueued after OAuth completion.
    /// </summary>
    public async Task InitialSyncAsync(Guid accountId)
    {
        if (!_options.Enabled) return;

        try
        {
            _logger.LogInformation("Starting initial email sync for account {AccountId}", accountId);

            var result = await _emailSyncService.SyncAccountAsync(
                accountId,
                triggeredById: null,
                triggerSource: EmailSyncTrigger.Initial);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Initial email sync completed for account {AccountId}: {Imported} notices imported",
                    accountId,
                    result.NoticesImported);
            }
            else
            {
                _logger.LogWarning(
                    "Initial email sync failed for account {AccountId}: {Error}",
                    accountId,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial email sync for account {AccountId}", accountId);
            throw;
        }
    }
}
