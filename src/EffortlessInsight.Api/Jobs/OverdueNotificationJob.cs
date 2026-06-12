using EffortlessInsight.Api.Services.Collaboration;
using Hangfire;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background job for sending overdue notifications
/// </summary>
public class OverdueNotificationJob
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<OverdueNotificationJob> _logger;

    public OverdueNotificationJob(
        INotificationService notificationService,
        ILogger<OverdueNotificationJob> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Check for overdue tasks and send notifications
    /// Scheduled to run daily at 9 AM
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task CheckOverdueTasksAsync()
    {
        _logger.LogInformation("Starting overdue tasks notification check");
        await _notificationService.NotifyOverdueTasksAsync();
        _logger.LogInformation("Completed overdue tasks notification check");
    }

    /// <summary>
    /// Check for overdue document requests and send notifications
    /// Scheduled to run daily at 9 AM
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task CheckOverdueDocumentRequestsAsync()
    {
        _logger.LogInformation("Starting overdue document requests notification check");
        await _notificationService.NotifyOverdueDocumentRequestsAsync();
        _logger.LogInformation("Completed overdue document requests notification check");
    }
}
