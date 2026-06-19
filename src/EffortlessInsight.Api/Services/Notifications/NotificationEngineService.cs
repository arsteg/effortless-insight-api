using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Core notification engine responsible for dispatching notifications across channels
/// </summary>
public class NotificationEngineService : INotificationEngineService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly INotificationPreferencesService _preferencesService;
    private readonly INotificationTemplateService _templateService;
    private readonly IEmailChannelService _emailService;
    private readonly ISmsChannelService _smsService;
    private readonly IPushChannelService _pushService;
    private readonly IWhatsAppChannelService _whatsAppService;
    private readonly IInAppChannelService _inAppService;
    private readonly IPushTokenService _pushTokenService;
    private readonly ILogger<NotificationEngineService> _logger;

    public NotificationEngineService(
        ApplicationDbContext dbContext,
        INotificationPreferencesService preferencesService,
        INotificationTemplateService templateService,
        IEmailChannelService emailService,
        ISmsChannelService smsService,
        IPushChannelService pushService,
        IWhatsAppChannelService whatsAppService,
        IInAppChannelService inAppService,
        IPushTokenService pushTokenService,
        ILogger<NotificationEngineService> logger)
    {
        _dbContext = dbContext;
        _preferencesService = preferencesService;
        _templateService = templateService;
        _emailService = emailService;
        _smsService = smsService;
        _pushService = pushService;
        _whatsAppService = whatsAppService;
        _inAppService = inAppService;
        _pushTokenService = pushTokenService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SendNotificationResponse> SendAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for notification", request.UserId);
            throw new KeyNotFoundException($"User {request.UserId} not found");
        }

        var priority = NotificationType.GetPriority(request.Type);
        var category = NotificationType.GetCategory(request.Type);

        // Evaluate which channels to use based on user preferences
        var channelDecision = await _preferencesService.EvaluateChannelsAsync(
            request.UserId, request.Type, priority, cancellationToken);

        // If quiet hours and not critical, schedule for later
        if (channelDecision.IsQuietHours && priority != NotificationPriority.Critical && channelDecision.DeliveryTime.HasValue)
        {
            var scheduledId = await ScheduleAsync(request, channelDecision.DeliveryTime.Value, cancellationToken);
            _logger.LogInformation("Notification scheduled for {Time} due to quiet hours", channelDecision.DeliveryTime.Value);
            return new SendNotificationResponse(scheduledId, [
                new DeliveryResultDto("scheduled", "pending", null)
            ]);
        }

        // Render notification content
        var rendered = await RenderNotificationContentAsync(request.Type, request.Data, "en", cancellationToken);

        // Create notification record
        var notification = new Notification
        {
            UserId = request.UserId,
            OrganizationId = user.OrganizationId,
            Type = request.Type,
            Category = category,
            Priority = priority,
            Title = rendered.Title,
            Body = rendered.Body,
            Data = request.Data,
            ActionUrl = GetActionUrl(request.Type, request.Data),
            ReferenceId = GetReferenceId(request.Data),
            ReferenceType = GetReferenceType(request.Type),
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var deliveries = new List<DeliveryResultDto>();

        // Send to each enabled channel
        var tasks = new List<Task<DeliveryResultDto>>();

        if (channelDecision.ShouldSendEmail || request.OverridePreferences)
        {
            tasks.Add(SendEmailAsync(notification, user, rendered, cancellationToken));
        }

        if (channelDecision.ShouldSendSms || (request.OverridePreferences && priority == NotificationPriority.Critical))
        {
            tasks.Add(SendSmsAsync(notification, user, rendered, cancellationToken));
        }

        if (channelDecision.ShouldSendPush || request.OverridePreferences)
        {
            tasks.Add(SendPushAsync(notification, user, request.Data, rendered, cancellationToken));
        }

        if (channelDecision.ShouldSendWhatsApp || (request.OverridePreferences && priority == NotificationPriority.Critical))
        {
            tasks.Add(SendWhatsAppAsync(notification, user, request.Type, request.Data, cancellationToken));
        }

        // In-app is always sent (unless explicitly disabled)
        if (channelDecision.ShouldSendInApp)
        {
            tasks.Add(SendInAppAsync(notification, user, cancellationToken));
        }

        // Wait for all deliveries to complete
        var results = await Task.WhenAll(tasks);
        deliveries.AddRange(results);

        _logger.LogInformation(
            "Sent notification {NotificationId} of type {Type} to user {UserId} via {ChannelCount} channels",
            notification.Id, request.Type, request.UserId, deliveries.Count);

        return new SendNotificationResponse(notification.Id, deliveries);
    }

    /// <inheritdoc />
    public async Task<BulkNotificationResponse> SendBulkAsync(BulkNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var batchId = request.BatchId ?? Guid.NewGuid().ToString();
        var results = new List<BulkResultItemDto>();
        var totalQueued = 0;
        var totalFailed = 0;

        foreach (var notification in request.Notifications)
        {
            try
            {
                var sendRequest = new SendNotificationRequest(
                    notification.UserId,
                    notification.Type,
                    notification.Data);

                var response = await SendAsync(sendRequest, cancellationToken);
                results.Add(new BulkResultItemDto(notification.UserId, response.NotificationId, true, null));
                totalQueued++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send bulk notification to user {UserId}", notification.UserId);
                results.Add(new BulkResultItemDto(notification.UserId, null, false, ex.Message));
                totalFailed++;
            }
        }

        _logger.LogInformation("Bulk notification batch {BatchId}: {Queued} queued, {Failed} failed",
            batchId, totalQueued, totalFailed);

        return new BulkNotificationResponse(batchId, totalQueued, totalFailed, results);
    }

    /// <inheritdoc />
    public async Task<NotificationListResponse> GetUserNotificationsAsync(
        Guid userId,
        string? status = null,
        string? type = null,
        DateTime? since = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Notifications
            .AsNoTracking()
            .Include(n => n.Deliveries)
            .Where(n => n.UserId == userId)
            .Where(n => n.DeletedAt == null);

        // Apply filters
        if (status == "read")
            query = query.Where(n => n.IsRead);
        else if (status == "unread")
            query = query.Where(n => !n.IsRead);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(n => n.Type == type || n.Category == type);

        if (since.HasValue)
            query = query.Where(n => n.CreatedAt >= since.Value);

        // Get total count
        var totalItems = await query.CountAsync(cancellationToken);

        // Get unread count
        var unreadCount = await _dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead && n.DeletedAt == null)
            .CountAsync(cancellationToken);

        // Apply pagination and ordering
        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = notifications.Select(MapToDto).ToList();

        return new NotificationListResponse(
            dtos,
            unreadCount,
            new PaginationInfo(page, pageSize, totalItems, (int)Math.Ceiling(totalItems / (double)pageSize)));
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead && n.DeletedAt == null)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MarkReadResponse> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);

        if (notification == null)
            throw new KeyNotFoundException($"Notification {notificationId} not found");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Send badge update via WebSocket
        var unreadCount = await GetUnreadCountAsync(userId, cancellationToken);
        await _inAppService.SendBadgeUpdateAsync(userId, unreadCount, cancellationToken);

        return new MarkReadResponse(notification.Id, true, notification.ReadAt ?? DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async Task<MarkAllReadResponse> MarkAllAsReadAsync(Guid userId, MarkAllReadRequest request, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead);

        if (request.BeforeDate.HasValue)
            query = query.Where(n => n.CreatedAt <= request.BeforeDate.Value);

        if (!string.IsNullOrEmpty(request.Type))
            query = query.Where(n => n.Type == request.Type || n.Category == request.Type);

        var now = DateTime.UtcNow;
        var count = await query.ExecuteUpdateAsync(
            s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, now),
            cancellationToken);

        // Send badge update
        var unreadCount = await GetUnreadCountAsync(userId, cancellationToken);
        await _inAppService.SendBadgeUpdateAsync(userId, unreadCount, cancellationToken);

        return new MarkAllReadResponse(count, unreadCount);
    }

    /// <inheritdoc />
    public async Task<Guid> ScheduleAsync(SendNotificationRequest request, DateTime scheduledFor, CancellationToken cancellationToken = default)
    {
        var scheduled = new ScheduledNotification
        {
            UserId = request.UserId,
            Type = request.Type,
            Data = request.Data,
            ScheduledFor = scheduledFor,
            Status = "pending"
        };

        _dbContext.ScheduledNotifications.Add(scheduled);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Scheduled notification {Id} for {Time}", scheduled.Id, scheduledFor);
        return scheduled.Id;
    }

    /// <inheritdoc />
    public async Task<bool> CancelScheduledAsync(Guid scheduledNotificationId, CancellationToken cancellationToken = default)
    {
        var scheduled = await _dbContext.ScheduledNotifications
            .FirstOrDefaultAsync(s => s.Id == scheduledNotificationId && s.Status == "pending", cancellationToken);

        if (scheduled == null)
            return false;

        scheduled.Status = "cancelled";
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cancelled scheduled notification {Id}", scheduledNotificationId);
        return true;
    }

    /// <inheritdoc />
    public async Task ProcessScheduledNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var dueNotifications = await _dbContext.ScheduledNotifications
            .Where(s => s.Status == "pending" && s.ScheduledFor <= DateTime.UtcNow)
            .Take(100)  // Process in batches
            .ToListAsync(cancellationToken);

        foreach (var scheduled in dueNotifications)
        {
            try
            {
                var request = new SendNotificationRequest(
                    scheduled.UserId,
                    scheduled.Type,
                    scheduled.Data);

                var response = await SendAsync(request, cancellationToken);

                scheduled.Status = "sent";
                scheduled.SentNotificationId = response.NotificationId;

                _logger.LogInformation("Processed scheduled notification {Id}", scheduled.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process scheduled notification {Id}", scheduled.Id);
                // Keep as pending for retry
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ProcessFailedDeliveriesAsync(CancellationToken cancellationToken = default)
    {
        var failedDeliveries = await _dbContext.NotificationDeliveries
            .Include(d => d.Notification)
                .ThenInclude(n => n.User)
            .Where(d => d.Status == DeliveryStatus.Failed &&
                        d.NextRetryAt <= DateTime.UtcNow &&
                        ((d.Channel == NotificationChannel.Email && d.RetryCount < 3) ||
                         (d.Channel == NotificationChannel.Sms && d.RetryCount < 3) ||
                         (d.Channel == NotificationChannel.Push && d.RetryCount < 2) ||
                         (d.Channel == NotificationChannel.WhatsApp && d.RetryCount < 1)))
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var delivery in failedDeliveries)
        {
            try
            {
                var result = await RetryDeliveryAsync(delivery, cancellationToken);

                if (result.Success)
                {
                    delivery.Status = DeliveryStatus.Sent;
                    delivery.ProviderMessageId = result.MessageId;
                    delivery.SentAt = DateTime.UtcNow;
                    _logger.LogInformation("Retry successful for delivery {Id}", delivery.Id);
                }
                else
                {
                    delivery.RetryCount++;
                    delivery.NextRetryAt = CalculateNextRetry(delivery.RetryCount, delivery.Channel);
                    delivery.FailureReason = result.ErrorMessage;
                    _logger.LogWarning("Retry failed for delivery {Id}, attempt {Attempt}", delivery.Id, delivery.RetryCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying delivery {Id}", delivery.Id);
                delivery.RetryCount++;
                delivery.NextRetryAt = CalculateNextRetry(delivery.RetryCount, delivery.Channel);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    #region Private Helper Methods

    private async Task<RenderedTemplate> RenderNotificationContentAsync(
        string type, Dictionary<string, object> data, string language, CancellationToken cancellationToken)
    {
        // Try to render from template service
        try
        {
            return await _templateService.RenderAsync(type, "default", data, language, cancellationToken);
        }
        catch
        {
            // Fallback to default content generation
            return GenerateDefaultContent(type, data);
        }
    }

    private RenderedTemplate GenerateDefaultContent(string type, Dictionary<string, object> data)
    {
        var title = type switch
        {
            NotificationType.Deadline1Day => "📅 Deadline Tomorrow",
            NotificationType.Deadline3Day => "📅 Deadline in 3 Days",
            NotificationType.Deadline7Day => "📅 Deadline in 7 Days",
            NotificationType.DeadlineToday => "🚨 Deadline Today",
            NotificationType.DeadlineMissed => "⚠️ Deadline Missed",
            NotificationType.NoticeHighRisk => "🔴 High-Risk Notice Detected",
            NotificationType.NoticeUploaded => "📤 Notice Uploaded",
            NotificationType.NoticeAnalyzed => "✅ Analysis Complete",
            NotificationType.TaskAssigned => "📋 Task Assigned",
            NotificationType.TaskOverdue => "⚠️ Task Overdue",
            NotificationType.TaskCompleted => "✅ Task Completed",
            NotificationType.UserMentioned => "💬 You were mentioned",
            NotificationType.DocumentRequested => "📄 Document Requested",
            NotificationType.DocumentReceived => "📥 Document Received",
            NotificationType.Welcome => "👋 Welcome to EffortlessInsight",
            NotificationType.LoginAlert => "🔐 New Login Detected",
            _ => "Notification"
        };

        var noticeNumber = data.GetValueOrDefault("noticeNumber")?.ToString() ?? "";
        var deadline = data.GetValueOrDefault("deadline")?.ToString() ?? "";
        var daysRemaining = data.GetValueOrDefault("daysRemaining")?.ToString() ?? "";

        var body = type switch
        {
            NotificationType.Deadline1Day => $"Notice #{noticeNumber} is due tomorrow ({deadline}). Take immediate action to avoid penalties.",
            NotificationType.Deadline3Day => $"Notice #{noticeNumber} deadline is in {daysRemaining} days ({deadline}).",
            NotificationType.Deadline7Day => $"Notice #{noticeNumber} deadline is in {daysRemaining} days ({deadline}).",
            NotificationType.DeadlineToday => $"Notice #{noticeNumber} is due TODAY! Immediate action required.",
            NotificationType.DeadlineMissed => $"Notice #{noticeNumber} deadline has passed. Please respond immediately.",
            NotificationType.NoticeHighRisk => $"High-risk notice #{noticeNumber} detected. Review required.",
            NotificationType.TaskAssigned => $"You have been assigned a new task for Notice #{noticeNumber}.",
            NotificationType.UserMentioned => $"Someone mentioned you in a comment on Notice #{noticeNumber}.",
            _ => "You have a new notification."
        };

        return new RenderedTemplate(title, body, title);
    }

    private string? GetActionUrl(string type, Dictionary<string, object> data)
    {
        var noticeId = data.GetValueOrDefault("noticeId")?.ToString();
        var taskId = data.GetValueOrDefault("taskId")?.ToString();
        var commentId = data.GetValueOrDefault("commentId")?.ToString();

        return type switch
        {
            _ when noticeId != null => $"/notices/{noticeId}",
            _ when taskId != null => $"/tasks/{taskId}",
            _ when commentId != null => $"/comments/{commentId}",
            _ => null
        };
    }

    private Guid? GetReferenceId(Dictionary<string, object> data)
    {
        var idStr = data.GetValueOrDefault("noticeId")?.ToString()
                    ?? data.GetValueOrDefault("taskId")?.ToString()
                    ?? data.GetValueOrDefault("commentId")?.ToString();

        return Guid.TryParse(idStr, out var id) ? id : null;
    }

    private string? GetReferenceType(string type)
    {
        var category = NotificationType.GetCategory(type);
        return category switch
        {
            NotificationCategory.Deadline or NotificationCategory.Notice or NotificationCategory.Sla => "Notice",
            NotificationCategory.Task => "Task",
            NotificationCategory.Collaboration => "Comment",
            _ => null
        };
    }

    private async Task<DeliveryResultDto> SendEmailAsync(
        Notification notification, ApplicationUser user, RenderedTemplate rendered, CancellationToken cancellationToken)
    {
        var delivery = new NotificationDelivery
        {
            NotificationId = notification.Id,
            Channel = NotificationChannel.Email,
            Status = DeliveryStatus.Pending
        };
        _dbContext.NotificationDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var email = user.Email;
            if (string.IsNullOrEmpty(email))
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.FailureReason = "No email address";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new DeliveryResultDto(NotificationChannel.Email, "failed", null);
            }

            var message = new EmailNotificationMessage(
                email,
                user.Name,
                rendered.Subject ?? rendered.Title,
                BuildEmailHtml(rendered, notification, user),
                null,
                notification.Id.ToString(),
                user.Id.ToString());

            var result = await _emailService.SendAsync(message, cancellationToken);

            delivery.Status = result.Success ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            delivery.ProviderMessageId = result.MessageId;
            delivery.SentAt = result.Success ? DateTime.UtcNow : null;
            delivery.FailureReason = result.ErrorMessage;
            delivery.NextRetryAt = result.Success ? null : CalculateNextRetry(0, NotificationChannel.Email);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.Email, delivery.Status, result.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email for notification {NotificationId}", notification.Id);
            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureReason = ex.Message;
            delivery.NextRetryAt = CalculateNextRetry(0, NotificationChannel.Email);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new DeliveryResultDto(NotificationChannel.Email, "failed", null);
        }
    }

    private async Task<DeliveryResultDto> SendSmsAsync(
        Notification notification, ApplicationUser user, RenderedTemplate rendered, CancellationToken cancellationToken)
    {
        var delivery = new NotificationDelivery
        {
            NotificationId = notification.Id,
            Channel = NotificationChannel.Sms,
            Status = DeliveryStatus.Pending
        };
        _dbContext.NotificationDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var phone = user.Mobile;
            if (string.IsNullOrEmpty(phone))
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.FailureReason = "No phone number";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new DeliveryResultDto(NotificationChannel.Sms, "failed", null);
            }

            // Truncate SMS to 160 characters
            var smsBody = TruncateSmsBody(rendered.Title + ": " + rendered.Body, notification.ActionUrl);

            var message = new SmsNotificationMessage(
                phone,
                smsBody,
                notification.Id.ToString(),
                notification.Priority == NotificationPriority.Critical);

            var result = await _smsService.SendAsync(message, cancellationToken);

            delivery.Status = result.Success ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            delivery.ProviderMessageId = result.MessageId;
            delivery.SentAt = result.Success ? DateTime.UtcNow : null;
            delivery.FailureReason = result.ErrorMessage;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.Sms, delivery.Status, result.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS for notification {NotificationId}", notification.Id);
            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureReason = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new DeliveryResultDto(NotificationChannel.Sms, "failed", null);
        }
    }

    private async Task<DeliveryResultDto> SendPushAsync(
        Notification notification, ApplicationUser user, Dictionary<string, object> data,
        RenderedTemplate rendered, CancellationToken cancellationToken)
    {
        var delivery = new NotificationDelivery
        {
            NotificationId = notification.Id,
            Channel = NotificationChannel.Push,
            Status = DeliveryStatus.Pending
        };
        _dbContext.NotificationDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var tokens = await _pushTokenService.GetActiveTokensAsync(user.Id, cancellationToken);
            if (!tokens.Any())
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.FailureReason = "No active push tokens";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new DeliveryResultDto(NotificationChannel.Push, "failed", null);
            }

            var pushData = new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString(),
                ["type"] = notification.Type,
                ["category"] = notification.Category
            };

            foreach (var kvp in data.Where(k => k.Value != null))
            {
                pushData[kvp.Key] = kvp.Value.ToString()!;
            }

            if (notification.ActionUrl != null)
                pushData["actionUrl"] = notification.ActionUrl;

            var message = new PushNotificationMessage(
                rendered.Title,
                rendered.Body,
                pushData,
                Priority: notification.Priority == NotificationPriority.Critical ? "high" : "normal",
                ChannelId: GetAndroidChannelId(notification.Type),
                DeepLink: $"effortlessinsight://notification/{notification.Id}",
                ActionUrl: notification.ActionUrl,
                NotificationId: notification.Id.ToString());

            var tokenStrings = tokens.Select(t => t.Token).ToList();
            var results = await _pushService.SendToTokensAsync(message, tokenStrings, cancellationToken);

            // Handle invalid tokens
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].Success && results[i].ErrorCode == "UNREGISTERED")
                {
                    await _pushTokenService.MarkTokenInvalidAsync(tokenStrings[i], cancellationToken);
                }
            }

            var anySuccess = results.Any(r => r.Success);
            delivery.Status = anySuccess ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            delivery.ProviderMessageId = results.FirstOrDefault(r => r.Success)?.MessageId;
            delivery.SentAt = anySuccess ? DateTime.UtcNow : null;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.Push, delivery.Status, delivery.ProviderMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push for notification {NotificationId}", notification.Id);
            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureReason = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new DeliveryResultDto(NotificationChannel.Push, "failed", null);
        }
    }

    private async Task<DeliveryResultDto> SendWhatsAppAsync(
        Notification notification, ApplicationUser user, string type,
        Dictionary<string, object> data, CancellationToken cancellationToken)
    {
        var delivery = new NotificationDelivery
        {
            NotificationId = notification.Id,
            Channel = NotificationChannel.WhatsApp,
            Status = DeliveryStatus.Pending
        };
        _dbContext.NotificationDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var phone = user.Mobile;
            if (string.IsNullOrEmpty(phone))
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.FailureReason = "No phone number";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new DeliveryResultDto(NotificationChannel.WhatsApp, "failed", null);
            }

            // WhatsApp requires pre-approved templates
            var templateSid = GetWhatsAppTemplateSid(type);
            if (string.IsNullOrEmpty(templateSid))
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.FailureReason = "No WhatsApp template for this notification type";
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new DeliveryResultDto(NotificationChannel.WhatsApp, "skipped", null);
            }

            var variables = BuildWhatsAppVariables(type, data, user);

            var message = new WhatsAppTemplateMessage(
                phone,
                templateSid,
                variables,
                notification.Id.ToString());

            var result = await _whatsAppService.SendTemplateAsync(message, cancellationToken);

            delivery.Status = result.Success ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            delivery.ProviderMessageId = result.MessageId;
            delivery.SentAt = result.Success ? DateTime.UtcNow : null;
            delivery.FailureReason = result.ErrorMessage;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.WhatsApp, delivery.Status, result.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp for notification {NotificationId}", notification.Id);
            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureReason = ex.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new DeliveryResultDto(NotificationChannel.WhatsApp, "failed", null);
        }
    }

    private async Task<DeliveryResultDto> SendInAppAsync(
        Notification notification, ApplicationUser user, CancellationToken cancellationToken)
    {
        var delivery = new NotificationDelivery
        {
            NotificationId = notification.Id,
            Channel = NotificationChannel.InApp,
            Status = DeliveryStatus.Pending
        };
        _dbContext.NotificationDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var message = new InAppNotificationMessage(
                user.Id,
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Body,
                notification.Data,
                notification.ActionUrl,
                notification.CreatedAt);

            var result = await _inAppService.SendAsync(message, cancellationToken);

            // In-app is always "delivered" once stored (WebSocket delivery is best-effort)
            delivery.Status = DeliveryStatus.Delivered;
            delivery.SentAt = DateTime.UtcNow;
            delivery.DeliveredAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.InApp, "delivered", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send in-app notification {NotificationId}", notification.Id);
            // In-app still succeeds because notification is stored
            delivery.Status = DeliveryStatus.Delivered;
            delivery.SentAt = DateTime.UtcNow;
            delivery.DeliveredAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new DeliveryResultDto(NotificationChannel.InApp, "delivered", null);
        }
    }

    private async Task<ChannelSendResult> RetryDeliveryAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        return delivery.Channel switch
        {
            NotificationChannel.Email => await RetryEmailAsync(delivery, cancellationToken),
            NotificationChannel.Sms => await RetrySmsAsync(delivery, cancellationToken),
            NotificationChannel.Push => await RetryPushAsync(delivery, cancellationToken),
            NotificationChannel.WhatsApp => await RetryWhatsAppAsync(delivery, cancellationToken),
            _ => new ChannelSendResult(false, null, "UNKNOWN_CHANNEL", "Unknown channel type")
        };
    }

    private async Task<ChannelSendResult> RetryEmailAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var notification = delivery.Notification;
        var user = notification.User;

        if (string.IsNullOrEmpty(user.Email))
            return new ChannelSendResult(false, null, "NO_EMAIL", "No email address");

        var rendered = GenerateDefaultContent(notification.Type, notification.Data);
        var message = new EmailNotificationMessage(
            user.Email,
            user.Name,
            rendered.Subject ?? rendered.Title,
            BuildEmailHtml(rendered, notification, user),
            NotificationId: notification.Id.ToString());

        return await _emailService.SendAsync(message, cancellationToken);
    }

    private async Task<ChannelSendResult> RetrySmsAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var notification = delivery.Notification;
        var user = notification.User;

        if (string.IsNullOrEmpty(user.Mobile))
            return new ChannelSendResult(false, null, "NO_PHONE", "No phone number");

        var rendered = GenerateDefaultContent(notification.Type, notification.Data);
        var message = new SmsNotificationMessage(
            user.Mobile,
            TruncateSmsBody(rendered.Title + ": " + rendered.Body, notification.ActionUrl),
            notification.Id.ToString());

        return await _smsService.SendAsync(message, cancellationToken);
    }

    private async Task<ChannelSendResult> RetryPushAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var notification = delivery.Notification;
        var user = notification.User;

        var tokens = await _pushTokenService.GetActiveTokensAsync(user.Id, cancellationToken);
        if (!tokens.Any())
            return new ChannelSendResult(false, null, "NO_TOKENS", "No active push tokens");

        var rendered = GenerateDefaultContent(notification.Type, notification.Data);
        var message = new PushNotificationMessage(
            rendered.Title,
            rendered.Body,
            new Dictionary<string, string> { ["notificationId"] = notification.Id.ToString() },
            NotificationId: notification.Id.ToString());

        var results = await _pushService.SendToTokensAsync(message, tokens.Select(t => t.Token).ToList(), cancellationToken);
        return results.FirstOrDefault() ?? new ChannelSendResult(false, null, "NO_RESULT", "No result from push service");
    }

    private async Task<ChannelSendResult> RetryWhatsAppAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        // WhatsApp retries are more complex due to template requirements
        // For now, we don't retry WhatsApp
        await Task.CompletedTask;
        return new ChannelSendResult(false, null, "WHATSAPP_NO_RETRY", "WhatsApp messages cannot be retried");
    }

    private static int GetMaxRetries(string channel) => channel switch
    {
        NotificationChannel.Email => 3,
        NotificationChannel.Sms => 3,
        NotificationChannel.Push => 2,
        NotificationChannel.WhatsApp => 1,
        _ => 1
    };

    private static DateTime CalculateNextRetry(int retryCount, string channel)
    {
        // Exponential backoff: 5, 15, 45 minutes for email/SMS; 5, 15 for push
        var baseMinutes = channel switch
        {
            NotificationChannel.Email or NotificationChannel.Sms => 5,
            _ => 5
        };

        var multiplier = Math.Pow(3, retryCount);
        return DateTime.UtcNow.AddMinutes(baseMinutes * multiplier);
    }

    private static string TruncateSmsBody(string body, string? actionUrl)
    {
        const int maxLength = 160;
        const int urlLength = 25;  // Shortened URL length

        var availableLength = actionUrl != null ? maxLength - urlLength - 1 : maxLength;

        if (body.Length <= availableLength)
            return actionUrl != null ? $"{body} {actionUrl}" : body;

        var truncated = body[..(availableLength - 3)] + "...";
        return actionUrl != null ? $"{truncated} {actionUrl}" : truncated;
    }

    private static string GetAndroidChannelId(string notificationType)
    {
        var priority = NotificationType.GetPriority(notificationType);
        return priority switch
        {
            NotificationPriority.Critical => "deadline_critical",
            NotificationPriority.High => "deadline_regular",
            _ => NotificationType.GetCategory(notificationType) switch
            {
                NotificationCategory.Task => "tasks",
                NotificationCategory.Collaboration => "collaboration",
                _ => "default"
            }
        };
    }

    private static string? GetWhatsAppTemplateSid(string type) => type switch
    {
        NotificationType.Deadline1Day or NotificationType.DeadlineToday => "deadline_reminder_v1",
        NotificationType.NoticeHighRisk => "high_risk_notice_v1",
        NotificationType.DocumentRequested => "document_request_v1",
        _ => null
    };

    private static Dictionary<string, string> BuildWhatsAppVariables(string type, Dictionary<string, object> data, ApplicationUser user)
    {
        var vars = new Dictionary<string, string>
        {
            ["1"] = user.Name  // User name is always first variable
        };

        if (data.TryGetValue("noticeNumber", out var noticeNumber))
            vars["2"] = noticeNumber?.ToString() ?? "";

        if (data.TryGetValue("deadline", out var deadline))
            vars["3"] = deadline?.ToString() ?? "";

        if (data.TryGetValue("daysRemaining", out var days))
            vars["4"] = days?.ToString() ?? "";

        return vars;
    }

    private string BuildEmailHtml(RenderedTemplate rendered, Notification notification, ApplicationUser user)
    {
        var alertColor = notification.Priority switch
        {
            NotificationPriority.Critical => "#EF4444",
            NotificationPriority.High => "#F59E0B",
            NotificationPriority.Medium => "#3B82F6",
            _ => "#10B981"
        };

        var actionUrl = notification.ActionUrl != null
            ? $"https://app.effortlessinsight.com{notification.ActionUrl}"
            : "https://app.effortlessinsight.com";

        return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{rendered.Title}</title>
</head>
<body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f9fafb;"">
  <div style=""background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.1);"">
    <div style=""background: #1e40af; padding: 20px; text-align: center;"">
      <h1 style=""color: white; margin: 0; font-size: 24px;"">EffortlessInsight</h1>
    </div>
    <div style=""background: {alertColor}; color: white; padding: 16px; text-align: center;"">
      <h2 style=""margin: 0; font-size: 18px;"">{rendered.Title}</h2>
    </div>
    <div style=""padding: 24px;"">
      <p style=""color: #374151; font-size: 16px;"">Hi {user.Name},</p>
      <p style=""color: #4b5563; font-size: 14px; line-height: 1.6;"">{rendered.Body}</p>
      <div style=""text-align: center; margin: 24px 0;"">
        <a href=""{actionUrl}"" style=""background: #1e40af; color: white; padding: 12px 32px; text-decoration: none; border-radius: 6px; display: inline-block; font-weight: bold;"">
          View Details
        </a>
      </div>
    </div>
    <div style=""padding: 16px; text-align: center; color: #6B7280; font-size: 12px; border-top: 1px solid #e5e7eb;"">
      <p>You're receiving this because you have notifications enabled.</p>
      <p>
        <a href=""https://app.effortlessinsight.com/settings/notifications"" style=""color: #1e40af;"">Manage Preferences</a> |
        <a href=""https://app.effortlessinsight.com/unsubscribe?token={GenerateUnsubscribeToken(user.Id)}"" style=""color: #1e40af;"">Unsubscribe</a>
      </p>
      <p>© {DateTime.UtcNow.Year} EffortlessInsight. All rights reserved.</p>
    </div>
  </div>
</body>
</html>";
    }

    private static string GenerateUnsubscribeToken(Guid userId)
    {
        // In production, this should be a signed JWT or encrypted token
        return Convert.ToBase64String(userId.ToByteArray());
    }

    private NotificationDto MapToDto(Notification n)
    {
        var channelStatus = n.Deliveries.Any() ? new NotificationDeliveryStatusDto(
            n.Deliveries.FirstOrDefault(d => d.Channel == NotificationChannel.Email) is { } email
                ? new ChannelDeliveryDto(true, email.DeliveredAt, email.OpenedAt, email.Status) : null,
            n.Deliveries.FirstOrDefault(d => d.Channel == NotificationChannel.Sms) is { } sms
                ? new ChannelDeliveryDto(true, sms.DeliveredAt, null, sms.Status) : null,
            n.Deliveries.FirstOrDefault(d => d.Channel == NotificationChannel.Push) is { } push
                ? new ChannelDeliveryDto(true, push.DeliveredAt, null, push.Status) : null,
            n.Deliveries.FirstOrDefault(d => d.Channel == NotificationChannel.WhatsApp) is { } wa
                ? new ChannelDeliveryDto(true, wa.DeliveredAt, null, wa.Status) : null,
            n.Deliveries.FirstOrDefault(d => d.Channel == NotificationChannel.InApp) is { } inApp
                ? new ChannelDeliveryDto(true, inApp.DeliveredAt, null, inApp.Status) : null
        ) : null;

        return new NotificationDto(
            n.Id,
            n.Type,
            n.Category,
            n.Priority,
            n.Title,
            n.Body,
            n.Data,
            n.ActionUrl,
            n.IsRead,
            n.ReadAt,
            n.CreatedAt,
            channelStatus);
    }

    #endregion
}
