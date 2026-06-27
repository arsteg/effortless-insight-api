using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Configuration options for data retention policies.
/// </summary>
public class DataRetentionOptions
{
    public const string SectionName = "DataRetention";

    /// <summary>
    /// Number of days to retain soft-deleted notices before permanent deletion.
    /// Default: 90 days (GDPR compliance).
    /// </summary>
    public int NoticeRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of days to retain soft-deleted attachments before permanent deletion.
    /// </summary>
    public int AttachmentRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of days to retain soft-deleted comments before permanent deletion.
    /// </summary>
    public int CommentRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of days to retain soft-deleted tasks before permanent deletion.
    /// </summary>
    public int TaskRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of days to retain audit logs. Set to 0 to keep indefinitely.
    /// Default: 365 days (1 year).
    /// </summary>
    public int AuditLogRetentionDays { get; set; } = 365;

    /// <summary>
    /// Number of days to retain login audit logs. Set to 0 to keep indefinitely.
    /// Default: 180 days (6 months).
    /// </summary>
    public int LoginAuditRetentionDays { get; set; } = 180;

    /// <summary>
    /// Number of days to retain expired sessions before cleanup.
    /// Default: 30 days.
    /// </summary>
    public int ExpiredSessionRetentionDays { get; set; } = 30;

    /// <summary>
    /// Maximum number of records to process per batch to prevent timeouts.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Whether to enable the data retention job. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to also delete associated files from storage when deleting attachments.
    /// </summary>
    public bool DeleteFilesFromStorage { get; set; } = true;
}

/// <summary>
/// Background job for enforcing data retention policies.
/// Permanently deletes soft-deleted records that have exceeded their retention period.
/// </summary>
public class DataRetentionJob
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _storageService;
    private readonly IAuditService _auditService;
    private readonly DataRetentionOptions _options;
    private readonly ILogger<DataRetentionJob> _logger;

    public DataRetentionJob(
        ApplicationDbContext db,
        IFileStorageService storageService,
        IAuditService auditService,
        IOptions<DataRetentionOptions> options,
        ILogger<DataRetentionJob> logger)
    {
        _db = db;
        _storageService = storageService;
        _auditService = auditService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Main entry point for the data retention cleanup job.
    /// Runs all retention policies in sequence.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [Queue("maintenance")]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Data retention job is disabled, skipping execution");
            return;
        }

        _logger.LogInformation("Starting data retention cleanup job");

        var totalDeleted = 0;

        try
        {
            // Clean up in order of dependencies (child records first)
            totalDeleted += await CleanupExpiredSessionsAsync(cancellationToken);
            totalDeleted += await CleanupLoginAuditLogsAsync(cancellationToken);
            totalDeleted += await CleanupAuditLogsAsync(cancellationToken);
            totalDeleted += await CleanupSoftDeletedCommentsAsync(cancellationToken);
            totalDeleted += await CleanupSoftDeletedAttachmentsAsync(cancellationToken);
            totalDeleted += await CleanupSoftDeletedTasksAsync(cancellationToken);
            totalDeleted += await CleanupSoftDeletedNoticesAsync(cancellationToken);

            _logger.LogInformation(
                "Data retention cleanup completed. Total records permanently deleted: {TotalDeleted}",
                totalDeleted);

            // Audit the cleanup operation
            await _auditService.LogAsync(new AuditLogEntry
            {
                Action = "system.data_retention_cleanup",
                EntityType = "System",
                NewValues = new Dictionary<string, object>
                {
                    ["total_deleted"] = totalDeleted,
                    ["executed_at"] = DateTime.UtcNow
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data retention cleanup job failed");
            throw;
        }
    }

    /// <summary>
    /// Permanently delete soft-deleted notices past retention period.
    /// </summary>
    private async Task<int> CleanupSoftDeletedNoticesAsync(CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.NoticeRetentionDays);
        var totalDeleted = 0;

        _logger.LogDebug(
            "Cleaning up notices soft-deleted before {CutoffDate} (retention: {Days} days)",
            cutoffDate, _options.NoticeRetentionDays);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Find notices to delete (bypassing global query filter)
            var noticesToDelete = await _db.Notices
                .IgnoreQueryFilters()
                .Where(n => n.DeletedAt != null && n.DeletedAt < cutoffDate)
                .Take(_options.BatchSize)
                .Select(n => new { n.Id, n.FileUrl })
                .ToListAsync(cancellationToken);

            if (noticesToDelete.Count == 0)
                break;

            // Delete associated files from storage
            if (_options.DeleteFilesFromStorage)
            {
                foreach (var notice in noticesToDelete)
                {
                    if (!string.IsNullOrEmpty(notice.FileUrl))
                    {
                        try
                        {
                            await _storageService.DeleteAsync(notice.FileUrl);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to delete file for notice {NoticeId}: {FileUrl}",
                                notice.Id, notice.FileUrl);
                        }
                    }
                }
            }

            // Permanently delete notices
            var ids = noticesToDelete.Select(n => n.Id).ToList();
            var deleted = await _db.Notices
                .IgnoreQueryFilters()
                .Where(n => ids.Contains(n.Id))
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deleted;
            _logger.LogDebug("Permanently deleted {Count} notices in this batch", deleted);
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Permanently deleted {Count} notices past {Days}-day retention period",
                totalDeleted, _options.NoticeRetentionDays);
        }

        return totalDeleted;
    }

    /// <summary>
    /// Permanently delete soft-deleted attachments past retention period.
    /// </summary>
    private async Task<int> CleanupSoftDeletedAttachmentsAsync(CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.AttachmentRetentionDays);
        var totalDeleted = 0;

        _logger.LogDebug(
            "Cleaning up attachments soft-deleted before {CutoffDate}",
            cutoffDate);

        while (!cancellationToken.IsCancellationRequested)
        {
            var attachmentsToDelete = await _db.Attachments
                .IgnoreQueryFilters()
                .Where(a => a.DeletedAt != null && a.DeletedAt < cutoffDate)
                .Take(_options.BatchSize)
                .Select(a => new { a.Id, a.FileUrl })
                .ToListAsync(cancellationToken);

            if (attachmentsToDelete.Count == 0)
                break;

            // Delete files from storage
            if (_options.DeleteFilesFromStorage)
            {
                foreach (var attachment in attachmentsToDelete)
                {
                    try
                    {
                        await _storageService.DeleteAsync(attachment.FileUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to delete file for attachment {AttachmentId}: {FileUrl}",
                            attachment.Id, attachment.FileUrl);
                    }
                }
            }

            var ids = attachmentsToDelete.Select(a => a.Id).ToList();
            var deleted = await _db.Attachments
                .IgnoreQueryFilters()
                .Where(a => ids.Contains(a.Id))
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deleted;
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Permanently deleted {Count} attachments past {Days}-day retention period",
                totalDeleted, _options.AttachmentRetentionDays);
        }

        return totalDeleted;
    }

    /// <summary>
    /// Permanently delete soft-deleted comments past retention period.
    /// </summary>
    private async Task<int> CleanupSoftDeletedCommentsAsync(CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.CommentRetentionDays);

        var deleted = await _db.Comments
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null && c.DeletedAt < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Permanently deleted {Count} comments past {Days}-day retention period",
                deleted, _options.CommentRetentionDays);
        }

        return deleted;
    }

    /// <summary>
    /// Permanently delete soft-deleted tasks past retention period.
    /// </summary>
    private async Task<int> CleanupSoftDeletedTasksAsync(CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.TaskRetentionDays);

        var deleted = await _db.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.DeletedAt != null && t.DeletedAt < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Permanently deleted {Count} tasks past {Days}-day retention period",
                deleted, _options.TaskRetentionDays);
        }

        return deleted;
    }

    /// <summary>
    /// Delete old audit logs past retention period.
    /// </summary>
    private async Task<int> CleanupAuditLogsAsync(CancellationToken cancellationToken)
    {
        if (_options.AuditLogRetentionDays <= 0)
            return 0; // Keep indefinitely

        var cutoffDate = DateTime.UtcNow.AddDays(-_options.AuditLogRetentionDays);

        var deleted = await _db.AuditLogs
            .Where(a => a.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} audit logs older than {Days} days",
                deleted, _options.AuditLogRetentionDays);
        }

        return deleted;
    }

    /// <summary>
    /// Delete old login audit logs past retention period.
    /// </summary>
    private async Task<int> CleanupLoginAuditLogsAsync(CancellationToken cancellationToken)
    {
        if (_options.LoginAuditRetentionDays <= 0)
            return 0; // Keep indefinitely

        var cutoffDate = DateTime.UtcNow.AddDays(-_options.LoginAuditRetentionDays);

        var deleted = await _db.LoginAudits
            .Where(l => l.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} login audit logs older than {Days} days",
                deleted, _options.LoginAuditRetentionDays);
        }

        return deleted;
    }

    /// <summary>
    /// Delete expired sessions past retention period.
    /// </summary>
    private async Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.ExpiredSessionRetentionDays);

        // Delete sessions that expired or were revoked before the cutoff date
        var deleted = await _db.UserSessions
            .Where(s => (s.RevokedAt != null || s.ExpiresAt < DateTime.UtcNow) && s.ExpiresAt < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Deleted {Count} expired sessions older than {Days} days",
                deleted, _options.ExpiredSessionRetentionDays);
        }

        return deleted;
    }
}

/// <summary>
/// Extension methods for configuring data retention background jobs.
/// </summary>
public static class DataRetentionJobsExtensions
{
    /// <summary>
    /// Configure recurring data retention jobs with Hangfire.
    /// </summary>
    public static void ConfigureDataRetentionJobs(WebApplication app)
    {
        // Run data retention cleanup daily at 3 AM UTC
        RecurringJob.AddOrUpdate<DataRetentionJob>(
            "data-retention-cleanup",
            job => job.ExecuteAsync(CancellationToken.None),
            "0 3 * * *", // Every day at 3 AM
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
