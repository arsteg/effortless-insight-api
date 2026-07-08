using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// GST Sync notification types
/// </summary>
public static class GstSyncNotificationTypes
{
    /// <summary>New notices synced from portal</summary>
    public const string NoticesSynced = "gst_sync.notices_synced";

    /// <summary>Daily digest of synced notices</summary>
    public const string DailyDigest = "gst_sync.daily_digest";

    /// <summary>Sync failed multiple times</summary>
    public const string SyncFailed = "gst_sync.sync_failed";

    /// <summary>Notice due date approaching</summary>
    public const string DueDateReminder = "gst_sync.due_date_reminder";

    /// <summary>Notice due date passed</summary>
    public const string DueDateOverdue = "gst_sync.due_date_overdue";

    /// <summary>Extension disconnected (no heartbeat)</summary>
    public const string ExtensionDisconnected = "gst_sync.extension_disconnected";

    /// <summary>GSTIN sync paused due to errors</summary>
    public const string SyncPaused = "gst_sync.sync_paused";

    /// <summary>Import to notices completed</summary>
    public const string ImportCompleted = "gst_sync.import_completed";
}

/// <summary>
/// Service for sending GST sync related notifications
/// </summary>
public interface IGstSyncNotificationService
{
    /// <summary>
    /// Send notification when new notices are synced
    /// </summary>
    Task NotifyNoticesSyncedAsync(Guid organizationId, Guid gstClientId, int newCount, int updatedCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send daily digest of synced notices
    /// </summary>
    Task SendDailyDigestAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send notification when sync fails
    /// </summary>
    Task NotifySyncFailedAsync(Guid organizationId, Guid gstClientId, string errorMessage, int consecutiveFailures, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send due date reminder
    /// </summary>
    Task NotifyDueDateReminderAsync(Guid noticeId, int daysUntilDue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send due date overdue notification
    /// </summary>
    Task NotifyDueDateOverdueAsync(Guid noticeId, int daysOverdue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send notification when extension is disconnected
    /// </summary>
    Task NotifyExtensionDisconnectedAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send notification when sync is paused due to errors
    /// </summary>
    Task NotifySyncPausedAsync(Guid organizationId, Guid gstClientId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send notification when notices are imported
    /// </summary>
    Task NotifyImportCompletedAsync(Guid userId, int importedCount, int failedCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process due date reminders for all organizations
    /// </summary>
    Task ProcessDueDateRemindersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of GST sync notification service
/// </summary>
public class GstSyncNotificationService : IGstSyncNotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationEngineService _notificationEngine;
    private readonly ILogger<GstSyncNotificationService> _logger;

    public GstSyncNotificationService(
        ApplicationDbContext context,
        INotificationEngineService notificationEngine,
        ILogger<GstSyncNotificationService> logger)
    {
        _context = context;
        _notificationEngine = notificationEngine;
        _logger = logger;
    }

    public async Task NotifyNoticesSyncedAsync(Guid organizationId, Guid gstClientId, int newCount, int updatedCount, CancellationToken cancellationToken = default)
    {
        if (newCount == 0 && updatedCount == 0) return;

        var client = await _context.GstClients
            .FirstOrDefaultAsync(c => c.Id == gstClientId, cancellationToken);

        if (client == null) return;

        // Get all users in the organization who should be notified
        var userIds = await GetOrganizationUserIdsAsync(organizationId, cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await _notificationEngine.SendAsync(new SendNotificationRequest(
                    UserId: userId,
                    Type: GstSyncNotificationTypes.NoticesSynced,
                    Data: new Dictionary<string, object>
                    {
                        ["gstin"] = client.Gstin,
                        ["clientName"] = client.TradeName ?? client.LegalName ?? client.Gstin,
                        ["newCount"] = newCount,
                        ["updatedCount"] = updatedCount,
                        ["totalCount"] = newCount + updatedCount,
                        ["dashboardUrl"] = "/gst-sync"
                    }
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notices synced notification to user {UserId}", userId);
            }
        }
    }

    public async Task SendDailyDigestAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        var today = DateTime.UtcNow.Date;

        // Get sync statistics for the last 24 hours
        var stats = await _context.GstNoticesRaw
            .Where(n => n.OrganizationId == organizationId && n.FirstSyncedAt >= yesterday && n.FirstSyncedAt < today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                NewNotices = g.Count(),
                TotalAmount = g.Sum(n => n.DemandAmount ?? 0),
                ByType = g.GroupBy(n => n.NoticeType).Select(t => new { Type = t.Key, Count = t.Count() }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (stats == null || stats.NewNotices == 0) return;

        // Get notices with approaching due dates
        var upcomingDueDates = await _context.GstNoticesRaw
            .Where(n => n.OrganizationId == organizationId &&
                       n.DueDate != null &&
                       n.DueDate >= DateOnly.FromDateTime(today) &&
                       n.DueDate <= DateOnly.FromDateTime(today.AddDays(7)))
            .CountAsync(cancellationToken);

        var userIds = await GetOrganizationUserIdsAsync(organizationId, cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await _notificationEngine.SendAsync(new SendNotificationRequest(
                    UserId: userId,
                    Type: GstSyncNotificationTypes.DailyDigest,
                    Data: new Dictionary<string, object>
                    {
                        ["date"] = yesterday.ToString("dd MMM yyyy"),
                        ["newNotices"] = stats.NewNotices,
                        ["totalAmount"] = stats.TotalAmount,
                        ["byType"] = stats.ByType,
                        ["upcomingDueDates"] = upcomingDueDates,
                        ["dashboardUrl"] = "/gst-sync"
                    }
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send daily digest to user {UserId}", userId);
            }
        }
    }

    public async Task NotifySyncFailedAsync(Guid organizationId, Guid gstClientId, string errorMessage, int consecutiveFailures, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients
            .FirstOrDefaultAsync(c => c.Id == gstClientId, cancellationToken);

        if (client == null) return;

        // Only notify on 3rd and 5th consecutive failure
        if (consecutiveFailures != 3 && consecutiveFailures != 5) return;

        var userIds = await GetOrganizationUserIdsAsync(organizationId, cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await _notificationEngine.SendAsync(new SendNotificationRequest(
                    UserId: userId,
                    Type: GstSyncNotificationTypes.SyncFailed,
                    Data: new Dictionary<string, object>
                    {
                        ["gstin"] = client.Gstin,
                        ["clientName"] = client.TradeName ?? client.LegalName ?? client.Gstin,
                        ["errorMessage"] = errorMessage,
                        ["consecutiveFailures"] = consecutiveFailures,
                        ["isPaused"] = consecutiveFailures >= 5,
                        ["settingsUrl"] = "/gst-sync"
                    }
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send sync failed notification to user {UserId}", userId);
            }
        }
    }

    public async Task NotifyDueDateReminderAsync(Guid noticeId, int daysUntilDue, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .Include(n => n.GstClient)
            .FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);

        if (notice == null) return;

        var userIds = await GetOrganizationUserIdsAsync(notice.OrganizationId, cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await _notificationEngine.SendAsync(new SendNotificationRequest(
                    UserId: userId,
                    Type: GstSyncNotificationTypes.DueDateReminder,
                    Data: new Dictionary<string, object>
                    {
                        ["noticeId"] = notice.PortalNoticeId,
                        ["noticeType"] = notice.NoticeType,
                        ["gstin"] = notice.Gstin,
                        ["clientName"] = notice.GstClient?.TradeName ?? notice.GstClient?.LegalName ?? notice.Gstin,
                        ["dueDate"] = notice.DueDate?.ToString("dd MMM yyyy") ?? "",
                        ["daysUntilDue"] = daysUntilDue,
                        ["demandAmount"] = notice.DemandAmount,
                        ["noticeUrl"] = $"/gst-sync?noticeId={notice.Id}"
                    }
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send due date reminder to user {UserId}", userId);
            }
        }
    }

    public async Task NotifyDueDateOverdueAsync(Guid noticeId, int daysOverdue, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .Include(n => n.GstClient)
            .FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);

        if (notice == null) return;

        var userIds = await GetOrganizationUserIdsAsync(notice.OrganizationId, cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await _notificationEngine.SendAsync(new SendNotificationRequest(
                    UserId: userId,
                    Type: GstSyncNotificationTypes.DueDateOverdue,
                    Data: new Dictionary<string, object>
                    {
                        ["noticeId"] = notice.PortalNoticeId,
                        ["noticeType"] = notice.NoticeType,
                        ["gstin"] = notice.Gstin,
                        ["clientName"] = notice.GstClient?.TradeName ?? notice.GstClient?.LegalName ?? notice.Gstin,
                        ["dueDate"] = notice.DueDate?.ToString("dd MMM yyyy") ?? "",
                        ["daysOverdue"] = daysOverdue,
                        ["demandAmount"] = notice.DemandAmount,
                        ["noticeUrl"] = $"/gst-sync?noticeId={notice.Id}"
                    }
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send due date overdue notification to user {UserId}", userId);
            }
        }
    }

    public async Task NotifyExtensionDisconnectedAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _notificationEngine.SendAsync(new SendNotificationRequest(
                UserId: userId,
                Type: GstSyncNotificationTypes.ExtensionDisconnected,
                Data: new Dictionary<string, object>
                {
                    ["extensionUrl"] = "https://chrome.google.com/webstore/detail/gst-notice-guard",
                    ["settingsUrl"] = "/gst-sync"
                }
            ), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send extension disconnected notification to user {UserId}", userId);
        }
    }

    public async Task NotifySyncPausedAsync(Guid organizationId, Guid gstClientId, string reason, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients
            .FirstOrDefaultAsync(c => c.Id == gstClientId, cancellationToken);

        if (client == null) return;

        var userIds = await GetOrganizationUserIdsAsync(organizationId, cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await _notificationEngine.SendAsync(new SendNotificationRequest(
                    UserId: userId,
                    Type: GstSyncNotificationTypes.SyncPaused,
                    Data: new Dictionary<string, object>
                    {
                        ["gstin"] = client.Gstin,
                        ["clientName"] = client.TradeName ?? client.LegalName ?? client.Gstin,
                        ["reason"] = reason,
                        ["settingsUrl"] = "/gst-sync"
                    }
                ), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send sync paused notification to user {UserId}", userId);
            }
        }
    }

    public async Task NotifyImportCompletedAsync(Guid userId, int importedCount, int failedCount, CancellationToken cancellationToken = default)
    {
        try
        {
            await _notificationEngine.SendAsync(new SendNotificationRequest(
                UserId: userId,
                Type: GstSyncNotificationTypes.ImportCompleted,
                Data: new Dictionary<string, object>
                {
                    ["importedCount"] = importedCount,
                    ["failedCount"] = failedCount,
                    ["totalCount"] = importedCount + failedCount,
                    ["noticesUrl"] = "/notices"
                }
            ), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send import completed notification to user {UserId}", userId);
        }
    }

    public async Task ProcessDueDateRemindersAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reminderDays = new[] { 1, 3, 7 }; // Remind 1, 3, and 7 days before

        foreach (var days in reminderDays)
        {
            var targetDate = today.AddDays(days);

            var notices = await _context.GstNoticesRaw
                .Where(n => n.DueDate == targetDate && !n.ImportedToNotices)
                .ToListAsync(cancellationToken);

            foreach (var notice in notices)
            {
                await NotifyDueDateReminderAsync(notice.Id, days, cancellationToken);
            }
        }

        // Also check for overdue notices
        var overdueNotices = await _context.GstNoticesRaw
            .Where(n => n.DueDate < today && n.DueDate >= today.AddDays(-7) && !n.ImportedToNotices)
            .ToListAsync(cancellationToken);

        foreach (var notice in overdueNotices)
        {
            var daysOverdue = today.DayNumber - notice.DueDate!.Value.DayNumber;
            if (daysOverdue == 1 || daysOverdue == 3 || daysOverdue == 7)
            {
                await NotifyDueDateOverdueAsync(notice.Id, daysOverdue, cancellationToken);
            }
        }
    }

    private async Task<List<Guid>> GetOrganizationUserIdsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // Get all active users in the organization
        return await _context.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.IsActive && !m.DeletedAt.HasValue)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
    }
}
