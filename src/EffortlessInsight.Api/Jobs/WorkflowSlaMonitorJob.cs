using EffortlessInsight.Api.Features.Workflows.Services;
using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background job for monitoring workflow SLAs and processing escalations.
/// </summary>
public class WorkflowSlaMonitorJob
{
    private readonly IWorkflowEngineService _workflowService;
    private readonly ILogger<WorkflowSlaMonitorJob> _logger;

    public WorkflowSlaMonitorJob(
        IWorkflowEngineService workflowService,
        ILogger<WorkflowSlaMonitorJob> logger)
    {
        _workflowService = workflowService;
        _logger = logger;
    }

    /// <summary>
    /// Updates SLA statuses for all active workflows.
    /// Should be run every 5 minutes.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task UpdateSlaStatuses(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting SLA status update job");

        try
        {
            await _workflowService.UpdateSlaStatusesAsync(cancellationToken);
            _logger.LogInformation("SLA status update completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating SLA statuses");
            throw;
        }
    }

    /// <summary>
    /// Processes SLA escalations for workflows at risk or breached.
    /// Should be run every 5 minutes after SLA update.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ProcessEscalations(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting escalation processing job");

        try
        {
            await _workflowService.ProcessEscalationsAsync(cancellationToken);
            _logger.LogInformation("Escalation processing completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing escalations");
            throw;
        }
    }
}

/// <summary>
/// Extension methods for registering workflow jobs.
/// </summary>
public static class WorkflowJobsExtensions
{
    /// <summary>
    /// Configures recurring workflow monitoring jobs.
    /// </summary>
    public static void ConfigureWorkflowJobs(this IApplicationBuilder app)
    {
        RecurringJob.AddOrUpdate<WorkflowSlaMonitorJob>(
            "workflow-sla-update",
            job => job.UpdateSlaStatuses(CancellationToken.None),
            "*/5 * * * *", // Every 5 minutes
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        RecurringJob.AddOrUpdate<WorkflowSlaMonitorJob>(
            "workflow-escalations",
            job => job.ProcessEscalations(CancellationToken.None),
            "2-57/5 * * * *", // Every 5 minutes, offset by 2 minutes from SLA update
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });
    }
}
