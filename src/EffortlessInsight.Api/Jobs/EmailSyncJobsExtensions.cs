using EffortlessInsight.Api.Options;
using Hangfire;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Extension methods for registering email sync background jobs.
/// </summary>
public static class EmailSyncJobsExtensions
{
    /// <summary>
    /// Registers recurring email sync background jobs with Hangfire.
    /// </summary>
    public static void UseEmailSyncJobs(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<EmailSyncOptions>>().Value;

        if (!options.Enabled)
        {
            RecurringJob.RemoveIfExists("email-sync-daily");
            RecurringJob.RemoveIfExists("email-sync-refresh-tokens");
            RecurringJob.RemoveIfExists("email-sync-cleanup");
            return;
        }

        // Daily sync job - runs at configured local time (default: 3 AM).
        RecurringJob.AddOrUpdate<EmailSyncJobs>(
            "email-sync-daily",
            job => job.ProcessDailySyncAsync(),
            options.SyncScheduleCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.SyncTimeZone) });

        // Token refresh job - runs every five minutes without scanning messages.
        RecurringJob.AddOrUpdate<EmailSyncJobs>(
            "email-sync-refresh-tokens",
            job => job.RefreshExpiringTokensAsync(),
            "*/5 * * * *", // Refresh-only: never scans mailbox messages
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Cleanup job - runs daily at 3 AM UTC
        RecurringJob.AddOrUpdate<EmailSyncJobs>(
            "email-sync-cleanup",
            job => job.CleanupAsync(),
            "0 3 * * *", // 3 AM UTC
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
