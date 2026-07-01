using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Extension methods for registering WhatsApp background jobs.
/// </summary>
public static class WhatsAppJobsExtensions
{
    /// <summary>
    /// Configure recurring WhatsApp jobs.
    /// </summary>
    public static void ConfigureWhatsAppJobs(this IApplicationBuilder app)
    {
        // Cleanup expired sessions (hourly)
        RecurringJob.AddOrUpdate<WhatsAppJobs>(
            "whatsapp-cleanup",
            job => job.CleanupExpiredSessionsAsync(CancellationToken.None),
            Cron.Hourly);

        // Sync templates from Meta (daily at 3 AM UTC)
        RecurringJob.AddOrUpdate<WhatsAppJobs>(
            "whatsapp-sync-templates",
            job => job.SyncTemplatesAsync(CancellationToken.None),
            "0 3 * * *");

        // Send daily digest (daily at 3:30 AM UTC = 9 AM IST)
        RecurringJob.AddOrUpdate<WhatsAppJobs>(
            "whatsapp-daily-digest",
            job => job.SendDailyDigestAsync(CancellationToken.None),
            "30 3 * * *");

        // Send deadline reminders (every hour at minute 15)
        RecurringJob.AddOrUpdate<WhatsAppJobs>(
            "whatsapp-deadline-reminders",
            job => job.SendDeadlineRemindersAsync(CancellationToken.None),
            "15 * * * *");

        // Retry failed messages (every 15 minutes)
        RecurringJob.AddOrUpdate<WhatsAppJobs>(
            "whatsapp-retry-failed",
            job => job.RetryFailedMessagesAsync(CancellationToken.None),
            "*/15 * * * *");
    }

    /// <summary>
    /// Queue a high-risk alert job.
    /// </summary>
    public static void QueueHighRiskAlert(Guid noticeId)
    {
        BackgroundJob.Enqueue<WhatsAppJobs>(
            job => job.SendHighRiskAlertAsync(noticeId, CancellationToken.None));
    }

    /// <summary>
    /// Queue a task assignment notification job.
    /// </summary>
    public static void QueueTaskAssignmentNotification(Guid taskId, Guid assignedById)
    {
        BackgroundJob.Enqueue<WhatsAppJobs>(
            job => job.SendTaskAssignmentAsync(taskId, assignedById, CancellationToken.None));
    }
}
