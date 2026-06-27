using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Extension methods for registering organization-related background jobs.
/// </summary>
public static class OrganizationJobsExtensions
{
    /// <summary>
    /// Configures recurring organization management jobs.
    /// </summary>
    public static void ConfigureOrganizationJobs(this IApplicationBuilder app)
    {
        // Hourly check for expired suspensions to unsuspend
        RecurringJob.AddOrUpdate<OrganizationJobs>(
            "organization-unsuspend-expired",
            job => job.UnsuspendExpiredMembersAsync(),
            "0 * * * *", // Every hour at minute 0
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Hourly check for expired invitations
        RecurringJob.AddOrUpdate<OrganizationJobs>(
            "organization-expire-invitations",
            job => job.ExpireInvitationsAsync(),
            "5 * * * *", // Every hour at minute 5
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Hourly check for expired CA access
        RecurringJob.AddOrUpdate<OrganizationJobs>(
            "organization-expire-ca-access",
            job => job.ExpireCaAccessAsync(),
            "10 * * * *", // Every hour at minute 10
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Daily cleanup of old invitations at 2 AM UTC
        RecurringJob.AddOrUpdate<OrganizationJobs>(
            "organization-cleanup-invitations",
            job => job.CleanupOldInvitationsAsync(),
            "0 2 * * *", // Every day at 2 AM
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        // Daily permanent deletion of soft-deleted organizations at 3 AM UTC
        RecurringJob.AddOrUpdate<OrganizationJobs>(
            "organization-permanent-delete",
            job => job.PermanentlyDeleteOrganizationsAsync(),
            "0 3 * * *", // Every day at 3 AM
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });
    }
}
