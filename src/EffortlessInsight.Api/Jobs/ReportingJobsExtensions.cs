using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Extension methods for configuring reporting background jobs.
/// </summary>
public static class ReportingJobsExtensions
{
    /// <summary>
    /// Configures recurring jobs for report scheduling and generation.
    /// </summary>
    public static void ConfigureReportingJobs(this WebApplication app)
    {
        // Process scheduled reports every 5 minutes
        RecurringJob.AddOrUpdate<ScheduledReportJob>(
            "scheduled-reports-processor",
            job => job.ProcessDueSchedulesAsync(),
            "*/5 * * * *", // Every 5 minutes
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        app.Logger.LogInformation("Configured recurring reporting jobs");
    }
}
