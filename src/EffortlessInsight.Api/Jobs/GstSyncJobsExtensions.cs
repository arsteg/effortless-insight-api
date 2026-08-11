using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Registers recurring GST sync notification jobs. Times are UTC; IST = UTC+5:30.
/// (These jobs existed but were never scheduled before this file.)
/// </summary>
public static class GstSyncJobsExtensions
{
    public static void ConfigureGstSyncJobs(this IApplicationBuilder app)
    {
        // Daily digest — 9:00 AM IST
        RecurringJob.AddOrUpdate<GstSyncNotificationJobs>(
            "gst-sync-daily-digest",
            job => job.SendDailyDigestsAsync(CancellationToken.None),
            "30 3 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Due date reminders — 8:00 AM IST
        RecurringJob.AddOrUpdate<GstSyncNotificationJobs>(
            "gst-sync-due-date-reminders",
            job => job.ProcessDueDateRemindersAsync(CancellationToken.None),
            "30 2 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Disconnected extension check — hourly at minute 40 (offset from GSTN jobs)
        RecurringJob.AddOrUpdate<GstSyncNotificationJobs>(
            "gst-sync-extension-check",
            job => job.CheckDisconnectedExtensionsAsync(CancellationToken.None),
            "40 * * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Stale client nudges — 7:30 AM IST daily
        RecurringJob.AddOrUpdate<GstSyncNotificationJobs>(
            "gst-sync-stale-clients",
            job => job.NotifyStaleClientsAsync(CancellationToken.None),
            "0 2 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Weekly digest — Monday 9:30 AM IST
        RecurringJob.AddOrUpdate<GstSyncNotificationJobs>(
            "gst-sync-weekly-digest",
            job => job.SendWeeklyDigestsAsync(CancellationToken.None),
            "0 4 * * 1",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
