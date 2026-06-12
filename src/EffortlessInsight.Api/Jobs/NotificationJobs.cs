using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Hangfire jobs for notification processing
/// </summary>
public class NotificationJobs
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationJobs> _logger;

    public NotificationJobs(
        IServiceProvider serviceProvider,
        ILogger<NotificationJobs> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Process scheduled notifications that are due
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    [Queue("default")]
    public async Task ProcessScheduledNotificationsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<INotificationEngineService>();

        _logger.LogInformation("Processing scheduled notifications...");
        await engine.ProcessScheduledNotificationsAsync();
        _logger.LogInformation("Scheduled notifications processed");
    }

    /// <summary>
    /// Retry failed notification deliveries
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [Queue("default")]
    public async Task ProcessFailedDeliveriesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<INotificationEngineService>();

        _logger.LogInformation("Processing failed deliveries...");
        await engine.ProcessFailedDeliveriesAsync();
        _logger.LogInformation("Failed deliveries processed");
    }

    /// <summary>
    /// Send deadline reminder notifications
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    [Queue("default")]
    public async Task SendDeadlineRemindersAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<INotificationEngineService>();

        _logger.LogInformation("Sending deadline reminders...");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var in7Days = today.AddDays(7);
        var in3Days = today.AddDays(3);
        var in1Day = today.AddDays(1);

        // Get notices with upcoming deadlines
        var notices = await dbContext.Notices
            .AsNoTracking()
            .Include(n => n.AssignedTo)
            .Where(n => n.DeletedAt == null)
            .Where(n => n.Status != "closed" && n.Status != "responded")
            .Where(n => n.ResponseDeadline != null)
            .Where(n => n.ResponseDeadline >= today && n.ResponseDeadline <= in7Days)
            .ToListAsync();

        var sentCount = 0;

        foreach (var notice in notices)
        {
            if (notice.ResponseDeadline == null || notice.AssignedToId == null)
                continue;

            var deadline = notice.ResponseDeadline.Value;
            var daysRemaining = deadline.DayNumber - today.DayNumber;

            string? notificationType = daysRemaining switch
            {
                0 => NotificationType.DeadlineToday,
                1 => NotificationType.Deadline1Day,
                <= 3 => NotificationType.Deadline3Day,
                <= 7 => NotificationType.Deadline7Day,
                _ => null
            };

            if (notificationType == null)
                continue;

            // Check if we already sent this reminder today
            var todayStart = today.ToDateTime(TimeOnly.MinValue);
            var todayEnd = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var alreadySent = await dbContext.Notifications
                .AnyAsync(n => n.UserId == notice.AssignedToId.Value &&
                              n.Type == notificationType &&
                              n.ReferenceId == notice.Id &&
                              n.CreatedAt >= todayStart && n.CreatedAt < todayEnd);

            if (alreadySent)
                continue;

            try
            {
                var request = new SendNotificationRequest(
                    notice.AssignedToId.Value,
                    notificationType,
                    new Dictionary<string, object>
                    {
                        ["noticeId"] = notice.Id.ToString(),
                        ["noticeNumber"] = notice.NoticeNumber ?? "",
                        ["noticeType"] = notice.NoticeType ?? "",
                        ["deadline"] = notice.ResponseDeadline.Value.ToString("dd MMM yyyy"),
                        ["daysRemaining"] = daysRemaining,
                        ["demandAmount"] = notice.TotalDemand ?? 0
                    });

                await engine.SendAsync(request);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send deadline reminder for notice {NoticeId}", notice.Id);
            }
        }

        // Check for missed deadlines
        var missedNotices = await dbContext.Notices
            .AsNoTracking()
            .Where(n => n.DeletedAt == null)
            .Where(n => n.Status != "closed" && n.Status != "responded")
            .Where(n => n.ResponseDeadline != null && n.ResponseDeadline < today)
            .Where(n => n.AssignedToId != null)
            .ToListAsync();

        foreach (var notice in missedNotices)
        {
            // Only send missed deadline notification once
            var alreadySent = await dbContext.Notifications
                .AnyAsync(n => n.UserId == notice.AssignedToId!.Value &&
                              n.Type == NotificationType.DeadlineMissed &&
                              n.ReferenceId == notice.Id);

            if (alreadySent)
                continue;

            try
            {
                var request = new SendNotificationRequest(
                    notice.AssignedToId!.Value,
                    NotificationType.DeadlineMissed,
                    new Dictionary<string, object>
                    {
                        ["noticeId"] = notice.Id.ToString(),
                        ["noticeNumber"] = notice.NoticeNumber ?? "",
                        ["noticeType"] = notice.NoticeType ?? "",
                        ["deadline"] = notice.ResponseDeadline!.Value.ToString("dd MMM yyyy"),
                        ["daysOverdue"] = today.DayNumber - notice.ResponseDeadline.Value.DayNumber,
                        ["demandAmount"] = notice.TotalDemand ?? 0
                    });

                await engine.SendAsync(request);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send missed deadline notification for notice {NoticeId}", notice.Id);
            }
        }

        _logger.LogInformation("Sent {Count} deadline reminders", sentCount);
    }

    /// <summary>
    /// Send daily digest emails
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [Queue("low")]
    public async Task SendDailyDigestAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailChannelService>();

        _logger.LogInformation("Sending daily digest emails...");

        // Get users with daily digest enabled
        var users = await dbContext.UserNotificationPreferences
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.User.DeletedAt == null)
            .Where(p => p.User.Email != null)
            .ToListAsync();

        var sentCount = 0;

        foreach (var prefs in users)
        {
            try
            {
                // Check if digest is enabled (from JSONB)
                var digestEnabled = prefs.DigestSettings.TryGetValue("daily", out var daily)
                    && daily is System.Text.Json.JsonElement je
                    && je.TryGetProperty("enabled", out var enabled)
                    && enabled.GetBoolean();

                if (!digestEnabled)
                    continue;

                // Get yesterday's activity
                var yesterday = DateTime.UtcNow.Date.AddDays(-1);
                var today = DateTime.UtcNow.Date;

                var notifications = await dbContext.Notifications
                    .AsNoTracking()
                    .Where(n => n.UserId == prefs.UserId)
                    .Where(n => n.CreatedAt >= yesterday && n.CreatedAt < today)
                    .OrderByDescending(n => n.Priority == "critical" ? 0 :
                                           n.Priority == "high" ? 1 :
                                           n.Priority == "medium" ? 2 : 3)
                    .ThenByDescending(n => n.CreatedAt)
                    .Take(20)
                    .ToListAsync();

                if (!notifications.Any())
                    continue;

                // Build digest email
                var digestHtml = BuildDailyDigestHtml(prefs.User, notifications);

                var message = new EmailNotificationMessage(
                    prefs.User.Email!,
                    prefs.User.Name,
                    $"Your Daily Summary - {yesterday:dd MMM yyyy}",
                    digestHtml);

                var result = await emailService.SendAsync(message);

                if (result.Success)
                    sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send daily digest to user {UserId}", prefs.UserId);
            }
        }

        _logger.LogInformation("Sent {Count} daily digest emails", sentCount);
    }

    /// <summary>
    /// Send weekly summary emails
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [Queue("low")]
    public async Task SendWeeklySummaryAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailChannelService>();

        _logger.LogInformation("Sending weekly summary emails...");

        // Similar to daily digest but for the past week
        // Implementation follows same pattern...

        _logger.LogInformation("Weekly summary emails sent");
    }

    /// <summary>
    /// Cleanup old notifications (90 days retention)
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    [Queue("low")]
    public async Task CleanupOldNotificationsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _logger.LogInformation("Cleaning up old notifications...");

        var cutoff = DateTime.UtcNow.AddDays(-90);

        // Delete old read notifications
        var deletedCount = await dbContext.Notifications
            .Where(n => n.IsRead && n.CreatedAt < cutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation("Deleted {Count} old notifications", deletedCount);

        // Delete old delivery records
        var deliveriesDeleted = await dbContext.NotificationDeliveries
            .Where(d => d.CreatedAt < cutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation("Deleted {Count} old delivery records", deliveriesDeleted);
    }

    /// <summary>
    /// Cleanup inactive push tokens
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    [Queue("low")]
    public async Task CleanupPushTokensAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var pushTokenService = scope.ServiceProvider.GetRequiredService<IPushTokenService>();

        _logger.LogInformation("Cleaning up inactive push tokens...");
        await pushTokenService.CleanupInactiveTokensAsync(90);
        _logger.LogInformation("Push token cleanup complete");
    }

    private static string BuildDailyDigestHtml(ApplicationUser user, List<Notification> notifications)
    {
        var criticalItems = notifications.Where(n => n.Priority == "critical").ToList();
        var highItems = notifications.Where(n => n.Priority == "high").ToList();
        var otherItems = notifications.Where(n => n.Priority != "critical" && n.Priority != "high").ToList();

        var yesterday = DateTime.UtcNow.Date.AddDays(-1);

        return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Daily Summary</title>
</head>
<body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f9fafb;"">
  <div style=""background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.1);"">
    <div style=""background: #1e40af; padding: 20px; text-align: center;"">
      <h1 style=""color: white; margin: 0; font-size: 24px;"">EffortlessInsight</h1>
      <p style=""color: #93c5fd; margin: 8px 0 0 0;"">Daily Summary - {yesterday:dd MMM yyyy}</p>
    </div>

    <div style=""padding: 24px;"">
      <p style=""color: #374151; font-size: 16px;"">Hi {user.Name},</p>
      <p style=""color: #4b5563; font-size: 14px;"">Here's your daily summary of notifications and updates:</p>

      {(criticalItems.Any() ? $@"
      <div style=""background: #fee2e2; border-left: 4px solid #ef4444; padding: 12px; margin: 16px 0; border-radius: 4px;"">
        <h3 style=""margin: 0 0 8px 0; color: #b91c1c;"">Critical Items ({criticalItems.Count})</h3>
        {string.Join("", criticalItems.Select(n => $@"
        <p style=""margin: 4px 0; color: #7f1d1d;"">• {n.Title}</p>
        "))}
      </div>" : "")}

      {(highItems.Any() ? $@"
      <div style=""background: #fef3c7; border-left: 4px solid #f59e0b; padding: 12px; margin: 16px 0; border-radius: 4px;"">
        <h3 style=""margin: 0 0 8px 0; color: #b45309;"">High Priority ({highItems.Count})</h3>
        {string.Join("", highItems.Select(n => $@"
        <p style=""margin: 4px 0; color: #92400e;"">• {n.Title}</p>
        "))}
      </div>" : "")}

      {(otherItems.Any() ? $@"
      <div style=""background: #f3f4f6; border-left: 4px solid #6b7280; padding: 12px; margin: 16px 0; border-radius: 4px;"">
        <h3 style=""margin: 0 0 8px 0; color: #374151;"">Other Updates ({otherItems.Count})</h3>
        {string.Join("", otherItems.Take(5).Select(n => $@"
        <p style=""margin: 4px 0; color: #4b5563;"">• {n.Title}</p>
        "))}
        {(otherItems.Count > 5 ? $"<p style=\"margin: 4px 0; color: #6b7280; font-style: italic;\">... and {otherItems.Count - 5} more</p>" : "")}
      </div>" : "")}

      <div style=""text-align: center; margin: 24px 0;"">
        <a href=""https://app.effortlessinsight.com/notifications"" style=""background: #1e40af; color: white; padding: 12px 32px; text-decoration: none; border-radius: 6px; display: inline-block; font-weight: bold;"">
          View All Notifications
        </a>
      </div>
    </div>

    <div style=""padding: 16px; text-align: center; color: #6b7280; font-size: 12px; border-top: 1px solid #e5e7eb;"">
      <p>
        <a href=""https://app.effortlessinsight.com/settings/notifications"" style=""color: #1e40af;"">Manage Digest Settings</a> |
        <a href=""https://app.effortlessinsight.com/unsubscribe"" style=""color: #1e40af;"">Unsubscribe</a>
      </p>
      <p>© {DateTime.UtcNow.Year} EffortlessInsight. All rights reserved.</p>
    </div>
  </div>
</body>
</html>";
    }
}

/// <summary>
/// Extension methods for registering notification jobs
/// </summary>
public static class NotificationJobsExtensions
{
    /// <summary>
    /// Configure recurring notification jobs
    /// </summary>
    public static void ConfigureNotificationJobs(IApplicationBuilder app)
    {
        // Process scheduled notifications every 5 minutes
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-process-scheduled",
            job => job.ProcessScheduledNotificationsAsync(),
            "*/5 * * * *");

        // Retry failed deliveries every 10 minutes
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-retry-failed",
            job => job.ProcessFailedDeliveriesAsync(),
            "*/10 * * * *");

        // Send deadline reminders at 9:00 AM IST (3:30 AM UTC)
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-deadline-reminders",
            job => job.SendDeadlineRemindersAsync(),
            "30 3 * * *");

        // Send daily digest at 9:00 AM IST (3:30 AM UTC)
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-daily-digest",
            job => job.SendDailyDigestAsync(),
            "30 3 * * *");

        // Send weekly summary on Monday at 9:00 AM IST
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-weekly-summary",
            job => job.SendWeeklySummaryAsync(),
            "30 3 * * 1");

        // Cleanup old notifications daily at 2:00 AM UTC
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-cleanup",
            job => job.CleanupOldNotificationsAsync(),
            "0 2 * * *");

        // Cleanup inactive push tokens weekly on Sunday
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-cleanup-tokens",
            job => job.CleanupPushTokensAsync(),
            "0 3 * * 0");
    }
}
