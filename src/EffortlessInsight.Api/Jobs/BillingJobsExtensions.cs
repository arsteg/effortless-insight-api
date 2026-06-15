using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Extension methods for configuring billing background jobs.
/// </summary>
public static class BillingJobsExtensions
{
    /// <summary>
    /// Configure recurring billing jobs with Hangfire.
    /// </summary>
    public static void ConfigureBillingJobs(WebApplication app)
    {
        // Process trial expirations - daily at midnight UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-process-trial-expirations",
            job => job.ProcessTrialExpirationsAsync(),
            "0 0 * * *", // Every day at midnight
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Process subscription renewals - daily at 6 AM UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-process-renewals",
            job => job.ProcessSubscriptionRenewalsAsync(),
            "0 6 * * *", // Every day at 6 AM
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Apply scheduled plan changes - daily at 1 AM UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-apply-scheduled-changes",
            job => job.ApplyScheduledPlanChangesAsync(),
            "0 1 * * *", // Every day at 1 AM
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Process grace period expirations - daily at 2 AM UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-process-grace-periods",
            job => job.ProcessGracePeriodExpirationsAsync(),
            "0 2 * * *", // Every day at 2 AM
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Reset monthly usage - 1st of each month at midnight UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-reset-monthly-usage",
            job => job.ResetMonthlyUsageAsync(),
            "0 0 1 * *", // First day of month at midnight
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Send usage warnings - every 6 hours
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-send-usage-warnings",
            job => job.SendUsageWarningsAsync(),
            "0 */6 * * *", // Every 6 hours
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Cleanup old webhook events - weekly on Sunday at 3 AM UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-cleanup-webhooks",
            job => job.CleanupWebhookEventsAsync(),
            "0 3 * * 0", // Every Sunday at 3 AM
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Retry failed webhook events - every hour
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-retry-failed-webhooks",
            job => job.RetryFailedWebhookEventsAsync(),
            "0 * * * *", // Every hour
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Send trial ending notifications - daily at 9 AM UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-send-trial-ending-notifications",
            job => job.SendTrialEndingNotificationsAsync(),
            "0 9 * * *", // Every day at 9 AM
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Send renewal reminders - daily at 9 AM UTC
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-send-renewal-reminders",
            job => job.SendRenewalRemindersAsync(),
            "0 9 * * *", // Every day at 9 AM
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Process payment retries - every 4 hours
        RecurringJob.AddOrUpdate<BillingJobs>(
            "billing-process-payment-retries",
            job => job.ProcessPaymentRetriesAsync(),
            "0 */4 * * *", // Every 4 hours
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
