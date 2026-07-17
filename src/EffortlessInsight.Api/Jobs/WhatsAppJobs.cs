using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.WhatsApp;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Hangfire jobs for WhatsApp bot functionality.
/// </summary>
public class WhatsAppJobs
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WhatsAppJobs> _logger;

    public WhatsAppJobs(
        IServiceProvider serviceProvider,
        ILogger<WhatsAppJobs> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Clean up expired sessions, verifications, and webhook events (hourly).
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    public async Task CleanupExpiredSessionsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting WhatsApp cleanup job");

        using var scope = _serviceProvider.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<IWhatsAppSessionService>();
        var verificationService = scope.ServiceProvider.GetRequiredService<IWhatsAppVerificationService>();
        var idempotencyService = scope.ServiceProvider.GetRequiredService<IWhatsAppWebhookIdempotencyService>();

        await sessionService.CleanupExpiredSessionsAsync(ct);
        await verificationService.CleanupExpiredVerificationsAsync(ct);
        await idempotencyService.CleanupOldEventsAsync(7, ct); // Keep 7 days

        _logger.LogInformation("Completed WhatsApp cleanup job");
    }

    /// <summary>
    /// Sync templates from Meta (daily at 3 AM).
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SyncTemplatesAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting WhatsApp template sync job");

        using var scope = _serviceProvider.CreateScope();
        var templateService = scope.ServiceProvider.GetRequiredService<IWhatsAppTemplateService>();

        await templateService.SyncTemplatesAsync(ct);

        _logger.LogInformation("Completed WhatsApp template sync job");
    }

    /// <summary>
    /// Send daily digest to opted-in users (daily at 9 AM IST).
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SendDailyDigestAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting WhatsApp daily digest job");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var botService = scope.ServiceProvider.GetRequiredService<IWhatsAppBotService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MetaWhatsAppOptions>>().Value;

        if (!options.DailyDigestEnabled)
        {
            _logger.LogInformation("Daily digest is disabled");
            return;
        }

        // Get users with WhatsApp linked and daily digest enabled
        var users = await db.Users
            .Where(u =>
                u.WhatsAppVerified &&
                u.WhatsAppOptedIn &&
                u.WhatsAppPhoneNumber != null &&
                u.OrganizationId != null &&
                u.DeletedAt == null)
            .ToListAsync(ct);

        _logger.LogInformation("Sending daily digest to {Count} users", users.Count);

        var today = DateTime.UtcNow.Date;
        var successCount = 0;
        var failCount = 0;

        foreach (var user in users)
        {
            try
            {
                // Get user's summary stats
                var pendingCount = await db.Notices
                    .Where(n => n.OrganizationId == user.OrganizationId && n.Status == "pending")
                    .CountAsync(ct);

                var dueTodayCount = await db.NoticeDeadlines
                    .Where(d => d.Notice.OrganizationId == user.OrganizationId &&
                               d.EffectiveDeadline.Date == today &&
                               d.Status != "completed")
                    .CountAsync(ct);

                var highRiskCount = await db.Notices
                    .Where(n => n.OrganizationId == user.OrganizationId &&
                               n.Priority == "high" &&
                               n.Status == "pending")
                    .CountAsync(ct);

                // Send template message
                var result = await botService.SendTemplateToUserAsync(
                    user.Id,
                    "daily_digest",
                    new Dictionary<string, string>
                    {
                        ["1"] = pendingCount.ToString(),
                        ["2"] = dueTodayCount.ToString(),
                        ["3"] = highRiskCount.ToString()
                    },
                    ct: ct);

                if (result.Success)
                    successCount++;
                else
                    failCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send daily digest to user {UserId}", user.Id);
                failCount++;
            }
        }

        _logger.LogInformation(
            "Completed daily digest job. Success: {Success}, Failed: {Failed}",
            successCount, failCount);
    }

    /// <summary>
    /// Send deadline reminders via WhatsApp (every hour).
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SendDeadlineRemindersAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting WhatsApp deadline reminders job");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var botService = scope.ServiceProvider.GetRequiredService<IWhatsAppBotService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MetaWhatsAppOptions>>().Value;

        if (!options.DeadlineRemindersEnabled)
        {
            _logger.LogInformation("Deadline reminders are disabled");
            return;
        }

        var today = DateTime.UtcNow.Date;
        var reminderDays = new[] { 1, 3, 7 }; // Remind at 7, 3, and 1 day before

        foreach (var days in reminderDays)
        {
            var targetDate = today.AddDays(days);

            // Find deadlines that need reminders
            var deadlines = await db.NoticeDeadlines
                .Include(d => d.Notice)
                    .ThenInclude(n => n.AssignedTo)
                .Where(d =>
                    d.EffectiveDeadline.Date == targetDate &&
                    d.Status != "completed" &&
                    d.Notice.AssignedTo != null &&
                    d.Notice.AssignedTo.WhatsAppVerified &&
                    d.Notice.AssignedTo.WhatsAppOptedIn)
                .ToListAsync(ct);

            foreach (var deadline in deadlines)
            {
                var user = deadline.Notice.AssignedTo!;

                try
                {
                    var result = await botService.SendTemplateToUserAsync(
                        user.Id,
                        "deadline_reminder",
                        new Dictionary<string, string>
                        {
                            ["1"] = deadline.Notice.NoticeType ?? "Notice",
                            ["2"] = deadline.EffectiveDeadline.ToString("MMMM d, yyyy"),
                            ["3"] = days.ToString()
                        },
                        ct: ct);

                    if (result.Success)
                    {
                        _logger.LogDebug(
                            "Sent deadline reminder to user {UserId} for deadline {DeadlineId}",
                            user.Id, deadline.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send deadline reminder to user {UserId}",
                        user.Id);
                }
            }
        }

        _logger.LogInformation("Completed deadline reminders job");
    }

    /// <summary>
    /// Send high-risk notice alerts via WhatsApp (on-demand, called when notice is created).
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SendHighRiskAlertAsync(Guid noticeId, CancellationToken ct)
    {
        _logger.LogInformation("Sending high-risk alert for notice {NoticeId}", noticeId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var botService = scope.ServiceProvider.GetRequiredService<IWhatsAppBotService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MetaWhatsAppOptions>>().Value;

        if (!options.HighRiskAlertsEnabled)
        {
            _logger.LogInformation("High-risk alerts are disabled");
            return;
        }

        var notice = await db.Notices
            .Include(n => n.Organization)
            .FirstOrDefaultAsync(n => n.Id == noticeId, ct);

        if (notice == null || notice.Priority?.ToLower() != "high")
        {
            return;
        }

        // Get the next deadline for this notice
        var deadline = await db.NoticeDeadlines
            .Where(d => d.NoticeId == noticeId && d.Status != "completed")
            .OrderBy(d => d.EffectiveDeadline)
            .FirstOrDefaultAsync(ct);

        // Get users in the organization who should receive alerts
        var users = await db.Users
            .Where(u =>
                u.OrganizationId == notice.OrganizationId &&
                u.WhatsAppVerified &&
                u.WhatsAppOptedIn &&
                u.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var user in users)
        {
            try
            {
                var result = await botService.SendTemplateToUserAsync(
                    user.Id,
                    "high_risk_notice",
                    new Dictionary<string, string>
                    {
                        ["1"] = notice.NoticeType ?? "Notice",
                        ["2"] = notice.TaxAmount?.ToString("N0") ?? "N/A",
                        ["3"] = deadline?.EffectiveDeadline.ToString("MMMM d") ?? "No deadline"
                    },
                    ct: ct);

                if (result.Success)
                {
                    _logger.LogDebug(
                        "Sent high-risk alert to user {UserId} for notice {NoticeId}",
                        user.Id, noticeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send high-risk alert to user {UserId}",
                    user.Id);
            }
        }
    }

    /// <summary>
    /// Send task assignment notification via WhatsApp.
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SendTaskAssignmentAsync(Guid taskId, Guid assignedById, CancellationToken ct)
    {
        _logger.LogInformation("Sending task assignment notification for task {TaskId}", taskId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var botService = scope.ServiceProvider.GetRequiredService<IWhatsAppBotService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MetaWhatsAppOptions>>().Value;

        if (!options.TaskAssignmentsEnabled)
        {
            return;
        }

        var task = await db.Tasks
            .Include(t => t.AssignedTo)
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task?.AssignedTo == null ||
            !task.AssignedTo.WhatsAppVerified ||
            !task.AssignedTo.WhatsAppOptedIn)
        {
            return;
        }

        var assignedBy = await db.Users.FindAsync([assignedById], ct);

        try
        {
            var result = await botService.SendTemplateToUserAsync(
                task.AssignedTo.Id,
                "task_assigned",
                new Dictionary<string, string>
                {
                    ["1"] = task.Title ?? "New Task",
                    ["2"] = task.DueDate?.ToString("MMMM d") ?? "No due date",
                    ["3"] = assignedBy?.Name ?? "Unknown"
                },
                ct: ct);

            if (result.Success)
            {
                _logger.LogDebug(
                    "Sent task assignment notification to user {UserId} for task {TaskId}",
                    task.AssignedTo.Id, taskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send task assignment notification to user {UserId}",
                task.AssignedTo.Id);
        }
    }

    /// <summary>
    /// Retry failed messages (every 15 minutes).
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    public async Task RetryFailedMessagesAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting WhatsApp retry failed messages job");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<IMetaWhatsAppClient>();
        var messageLogService = scope.ServiceProvider.GetRequiredService<IWhatsAppMessageLogService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<MetaWhatsAppOptions>>().Value;

        var failedMessages = await messageLogService.GetFailedMessagesForRetryAsync(options.MaxRetryAttempts, ct);

        _logger.LogInformation("Found {Count} failed messages to retry", failedMessages.Count);

        var successCount = 0;
        var failCount = 0;

        foreach (var message in failedMessages)
        {
            try
            {
                // Get the phone number - prefer FullPhoneNumber (stored for retryable messages)
                // Fall back to user's WhatsApp number if available
                string? phoneNumber = message.FullPhoneNumber;
                if (string.IsNullOrEmpty(phoneNumber) && message.UserId.HasValue)
                {
                    var user = await db.Users.FindAsync([message.UserId.Value], ct);
                    phoneNumber = user?.WhatsAppPhoneNumber;
                }

                if (string.IsNullOrEmpty(phoneNumber))
                {
                    _logger.LogWarning("Cannot retry message {MessageId}: no phone number available", message.Id);
                    await messageLogService.MarkRetryAttemptAsync(
                        message.Id,
                        success: false,
                        newWamId: null,
                        errorCode: "NO_PHONE",
                        errorMessage: "No phone number available for retry",
                        ct);
                    failCount++;
                    continue;
                }

                WhatsAppSendResult result;

                if (!string.IsNullOrEmpty(message.TemplateName))
                {
                    // Retry template message using stored parameters
                    if (message.TemplateParameters == null || message.TemplateParameters.Count == 0)
                    {
                        _logger.LogWarning("Cannot retry template message {MessageId}: no parameters stored", message.Id);
                        await messageLogService.MarkRetryAttemptAsync(
                            message.Id,
                            success: false,
                            newWamId: null,
                            errorCode: "NO_PARAMS",
                            errorMessage: "Template parameters not stored for retry",
                            ct);
                        failCount++;
                        continue;
                    }

                    // Convert List<string> to List<TemplateParameter> for the client
                    var bodyParameters = message.TemplateParameters
                        .Select(p => new TemplateParameter("text", p))
                        .ToList();

                    result = await client.SendTemplateMessageAsync(
                        phoneNumber,
                        message.TemplateName,
                        message.TemplateLanguage ?? "en",
                        bodyParameters,
                        ct: ct);
                }
                else if (!string.IsNullOrEmpty(message.Content))
                {
                    result = await client.SendTextMessageAsync(phoneNumber, message.Content, ct: ct);
                }
                else
                {
                    _logger.LogWarning("Cannot retry message {MessageId}: no content or template", message.Id);
                    failCount++;
                    continue;
                }

                // Update message log using the service method
                await messageLogService.MarkRetryAttemptAsync(
                    message.Id,
                    success: result.Success,
                    newWamId: result.MessageId,
                    errorCode: result.ErrorCode,
                    errorMessage: result.ErrorMessage,
                    ct);

                if (result.Success)
                {
                    successCount++;
                    _logger.LogDebug("Successfully retried message {MessageId}", message.Id);
                }
                else
                {
                    failCount++;
                    _logger.LogWarning(
                        "Retry failed for message {MessageId}: {ErrorCode} - {ErrorMessage}",
                        message.Id, result.ErrorCode, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while retrying message {MessageId}", message.Id);
                failCount++;

                // Mark the retry attempt as failed
                try
                {
                    await messageLogService.MarkRetryAttemptAsync(
                        message.Id,
                        success: false,
                        newWamId: null,
                        errorCode: "EXCEPTION",
                        errorMessage: ex.Message.Length > 500 ? ex.Message[..500] : ex.Message,
                        ct);
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Failed to mark retry attempt for message {MessageId}", message.Id);
                }
            }
        }

        _logger.LogInformation(
            "Completed retry failed messages job. Success: {Success}, Failed: {Failed}",
            successCount, failCount);
    }
}
