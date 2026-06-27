using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Reporting;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background job for executing scheduled reports.
/// GAP-RPT-006: Scheduled Reports execution.
/// </summary>
public class ScheduledReportJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IReportBuilderService _reportService;
    private readonly IEmailService _emailService;
    private readonly ILogger<ScheduledReportJob> _logger;

    // Maximum consecutive failures before disabling a schedule
    private const int MaxConsecutiveFailures = 5;

    public ScheduledReportJob(
        ApplicationDbContext dbContext,
        IReportBuilderService reportService,
        IEmailService emailService,
        ILogger<ScheduledReportJob> logger)
    {
        _dbContext = dbContext;
        _reportService = reportService;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// Processes all due scheduled reports.
    /// This method is called by Hangfire on a recurring schedule.
    /// </summary>
    public async Task ProcessDueSchedulesAsync()
    {
        var now = DateTime.UtcNow;

        _logger.LogDebug("Checking for due scheduled reports at {Time}", now);

        // Find all active schedules that are due
        var dueSchedules = await _dbContext.Set<ReportSchedule>()
            .Include(s => s.SavedReport)
            .ThenInclude(r => r.CreatedBy)
            .Where(s => s.IsActive &&
                        s.DeletedAt == null &&
                        s.NextRunAt.HasValue &&
                        s.NextRunAt <= now &&
                        s.SavedReport.DeletedAt == null)
            .ToListAsync();

        if (dueSchedules.Count == 0)
        {
            _logger.LogDebug("No scheduled reports due for execution");
            return;
        }

        _logger.LogInformation("Found {Count} scheduled reports due for execution", dueSchedules.Count);

        foreach (var schedule in dueSchedules)
        {
            try
            {
                await ExecuteScheduledReportAsync(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error processing schedule {ScheduleId} for report {ReportId}",
                    schedule.Id, schedule.SavedReportId);
            }
        }
    }

    /// <summary>
    /// Executes a single scheduled report.
    /// </summary>
    private async Task ExecuteScheduledReportAsync(ReportSchedule schedule)
    {
        var report = schedule.SavedReport;

        _logger.LogInformation(
            "Executing scheduled report {ScheduleId} for '{ReportName}' (Report: {ReportId})",
            schedule.Id, report.Name, report.Id);

        try
        {
            // Mark as pending
            schedule.LastRunStatus = ScheduleRunStatus.Pending;
            await _dbContext.SaveChangesAsync();

            // Export the report
            var exportResult = await _reportService.ExportReportAsync(
                report.Id,
                report.CreatedById,
                schedule.ExportFormat);

            // Send to all recipients
            var successCount = 0;
            var failedRecipients = new List<string>();

            foreach (var recipient in schedule.Recipients)
            {
                try
                {
                    await SendReportEmailAsync(
                        recipient,
                        report.Name,
                        schedule.ExportFormat,
                        exportResult.DownloadUrl,
                        exportResult.ExpiresAt);

                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to send scheduled report to {Recipient}",
                        recipient);
                    failedRecipients.Add(recipient);
                }
            }

            // Update schedule status
            schedule.LastRunAt = DateTime.UtcNow;
            schedule.LastRunStatus = failedRecipients.Count == 0
                ? ScheduleRunStatus.Success
                : (successCount > 0 ? ScheduleRunStatus.Success : ScheduleRunStatus.Failed);
            schedule.LastRunError = failedRecipients.Count > 0
                ? $"Failed to send to: {string.Join(", ", failedRecipients)}"
                : null;
            schedule.ConsecutiveFailures = 0;
            schedule.NextRunAt = CalculateNextRunTime(schedule);

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Successfully executed scheduled report {ScheduleId}. Sent to {SuccessCount}/{TotalCount} recipients. Next run: {NextRun}",
                schedule.Id, successCount, schedule.Recipients.Count, schedule.NextRunAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to execute scheduled report {ScheduleId} for report {ReportId}",
                schedule.Id, report.Id);

            // Update failure status
            schedule.LastRunAt = DateTime.UtcNow;
            schedule.LastRunStatus = ScheduleRunStatus.Failed;
            schedule.LastRunError = ex.Message.Length > 1000
                ? ex.Message[..1000]
                : ex.Message;
            schedule.ConsecutiveFailures++;

            // Disable schedule if too many consecutive failures
            if (schedule.ConsecutiveFailures >= MaxConsecutiveFailures)
            {
                schedule.IsActive = false;
                _logger.LogWarning(
                    "Disabled schedule {ScheduleId} after {Failures} consecutive failures",
                    schedule.Id, schedule.ConsecutiveFailures);
            }
            else
            {
                // Still calculate next run time for retry
                schedule.NextRunAt = CalculateNextRunTime(schedule);
            }

            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Sends the scheduled report email to a recipient.
    /// </summary>
    private async Task SendReportEmailAsync(
        string recipient,
        string reportName,
        string format,
        string downloadUrl,
        DateTime expiresAt)
    {
        var formatDisplay = format.ToUpperInvariant();
        var expiryTime = expiresAt.ToString("MMMM dd, yyyy 'at' HH:mm 'UTC'");

        var subject = $"Scheduled Report: {reportName}";
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2563eb; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9fafb; }}
        .button {{ display: inline-block; padding: 12px 24px; background-color: #2563eb; color: white; text-decoration: none; border-radius: 6px; margin: 20px 0; }}
        .footer {{ padding: 20px; text-align: center; font-size: 12px; color: #6b7280; }}
        .warning {{ background-color: #fef3c7; padding: 10px; border-radius: 4px; margin-top: 15px; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>EffortlessInsight</h1>
        </div>
        <div class=""content"">
            <h2>Your Scheduled Report is Ready</h2>
            <p>Your scheduled report <strong>{reportName}</strong> has been generated and is ready for download.</p>

            <p><strong>Format:</strong> {formatDisplay}</p>

            <p style=""text-align: center;"">
                <a href=""{downloadUrl}"" class=""button"">Download Report</a>
            </p>

            <div class=""warning"">
                <strong>Note:</strong> This download link will expire on {expiryTime}. Please download your report before then.
            </div>
        </div>
        <div class=""footer"">
            <p>This is an automated message from EffortlessInsight.</p>
            <p>You received this email because you are subscribed to scheduled reports.</p>
        </div>
    </div>
</body>
</html>";

        await _emailService.SendAsync(recipient, subject, htmlBody);
    }

    /// <summary>
    /// Calculates the next run time based on the schedule configuration.
    /// </summary>
    private static DateTime? CalculateNextRunTime(ReportSchedule schedule)
    {
        if (!schedule.IsActive)
            return null;

        var now = DateTime.UtcNow;
        TimeZoneInfo timeZone;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        }
        catch
        {
            // Fall back to UTC if timezone is invalid
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone);
        var targetTime = schedule.TimeOfDay;

        DateTime nextRun;

        switch (schedule.Frequency.ToLowerInvariant())
        {
            case ScheduleFrequency.Daily:
                nextRun = localNow.Date.Add(targetTime.ToTimeSpan());
                if (nextRun <= localNow)
                    nextRun = nextRun.AddDays(1);
                break;

            case ScheduleFrequency.Weekly:
                var dayOfWeek = schedule.DayOfWeek ?? 1; // Default Monday
                var daysUntilTarget = ((dayOfWeek - (int)localNow.DayOfWeek) + 7) % 7;
                if (daysUntilTarget == 0)
                {
                    // Same day - check if time has passed
                    nextRun = localNow.Date.Add(targetTime.ToTimeSpan());
                    if (nextRun <= localNow)
                        nextRun = nextRun.AddDays(7);
                }
                else
                {
                    nextRun = localNow.Date.AddDays(daysUntilTarget).Add(targetTime.ToTimeSpan());
                }
                break;

            case ScheduleFrequency.Monthly:
                var dayOfMonth = schedule.DayOfMonth ?? 1;
                var currentMonth = new DateTime(localNow.Year, localNow.Month, 1);
                var daysInCurrentMonth = DateTime.DaysInMonth(localNow.Year, localNow.Month);
                var targetDay = Math.Min(dayOfMonth, daysInCurrentMonth);

                nextRun = currentMonth.AddDays(targetDay - 1).Add(targetTime.ToTimeSpan());

                if (nextRun <= localNow)
                {
                    // Move to next month
                    var nextMonth = currentMonth.AddMonths(1);
                    var daysInNextMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                    targetDay = Math.Min(dayOfMonth, daysInNextMonth);
                    nextRun = nextMonth.AddDays(targetDay - 1).Add(targetTime.ToTimeSpan());
                }
                break;

            default:
                return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(nextRun, timeZone);
    }
}
