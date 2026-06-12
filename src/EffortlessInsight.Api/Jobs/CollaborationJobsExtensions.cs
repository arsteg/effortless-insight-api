using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Extension methods for registering collaboration-related background jobs.
/// </summary>
public static class CollaborationJobsExtensions
{
    /// <summary>
    /// Configures recurring collaboration jobs for task and document request monitoring.
    /// </summary>
    public static void ConfigureCollaborationJobs(this IApplicationBuilder app)
    {
        // Daily overdue task notifications at 9 AM UTC
        RecurringJob.AddOrUpdate<OverdueNotificationJob>(
            "collaboration-overdue-tasks",
            job => job.CheckOverdueTasksAsync(),
            "0 9 * * *", // Every day at 9 AM
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Daily overdue document request notifications at 9:05 AM UTC
        RecurringJob.AddOrUpdate<OverdueNotificationJob>(
            "collaboration-overdue-documents",
            job => job.CheckOverdueDocumentRequestsAsync(),
            "5 9 * * *", // Every day at 9:05 AM
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });
    }
}
