using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Extension methods for registering GSTN-related background jobs.
/// </summary>
public static class GstnJobsExtensions
{
    /// <summary>
    /// Configures recurring GSTN integration jobs.
    /// Job schedules are staggered to avoid race conditions.
    /// </summary>
    public static void ConfigureGstnJobs(this IApplicationBuilder app)
    {
        // Hourly token refresh for connections expiring soon
        // Runs at minute 5 - BEFORE sync jobs to ensure valid tokens
        RecurringJob.AddOrUpdate<GstnJobs>(
            "gstn-token-refresh",
            job => job.RefreshExpiringTokensAsync(),
            "5 * * * *", // Every hour at minute 5
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Process scheduled syncs every 15 minutes
        // Runs at 2, 17, 32, 47 - offset from token refresh
        RecurringJob.AddOrUpdate<GstnJobs>(
            "gstn-notice-sync",
            job => job.ProcessScheduledSyncsAsync(),
            "2,17,32,47 * * * *", // Every 15 minutes, offset by 2
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Daily cleanup at 4 AM UTC
        RecurringJob.AddOrUpdate<GstnJobs>(
            "gstn-cleanup",
            job => job.CleanupAsync(),
            "0 4 * * *", // Every day at 4 AM
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Validate connections every 6 hours
        // Runs at minute 35 to avoid collision with other jobs
        RecurringJob.AddOrUpdate<GstnJobs>(
            "gstn-validate-connections",
            job => job.ValidateConnectionsAsync(),
            "35 */6 * * *", // Every 6 hours at minute 35
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });
    }
}
