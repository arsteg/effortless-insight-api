using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IConfiguration _configuration;
    // Resolved lazily to avoid a constructor DI cycle: DeadLetterService depends
    // on INotificationEngineService, so the engine cannot take IDeadLetterService
    // as a constructor dependency (audit BE-04).
    private readonly IServiceProvider _serviceProvider;
    private readonly IBackgroundJobClient _backgroundJobs;

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
        ILogger<NotificationEngineService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IBackgroundJobClient backgroundJobs)
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
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _backgroundJobs = backgroundJobs;
    }

    // BE-09: when true, SendAsync persists the notification and enqueues a
    // Hangfire job for the channel fan-out instead of sending inline. Default
    // false — inline behavior is unchanged unless explicitly enabled.
    private bool UseQueue =>
        _configuration.GetValue("Notifications:UseQueue", false);

    // BE-10: when > 0, a notification with the same (user, type, reference) sent
    // within this window is treated as a duplicate and skipped. Default 0 = off.
    private int DeduplicationWindowMinutes =>
        _configuration.GetValue("Notifications:DeduplicationWindowMinutes", 0);

    // Per-user, per-channel rate limiting. When enabled, once a user has been
    // sent more than the per-channel cap within the rolling window, that channel
    // is suppressed for further non-critical notifications (in-app always still
    // records them). Prevents notification storms — e.g. a GST sync importing
    // 200 notices firing 200 pushes. Default off.
    // Marker used in a SendNotificationResponse's deliveries when a send was
    // deferred to a ScheduledNotification instead of dispatched (audit BE-27).
    private const string ScheduledChannelMarker = "scheduled";

    private bool RateLimitEnabled =>
        _configuration.GetValue("Notifications:RateLimitEnabled", false);
    private int RateLimitWindowMinutes =>
        _configuration.GetValue("Notifications:RateLimitWindowMinutes", 60);
    private int RateLimitEmail => _configuration.GetValue("Notifications:RateLimitEmailPerWindow", 100);
    private int RateLimitSms => _configuration.GetValue("Notifications:RateLimitSmsPerWindow", 10);
    private int RateLimitPush => _configuration.GetValue("Notifications:RateLimitPushPerWindow", 50);
    private int RateLimitWhatsApp => _configuration.GetValue("Notifications:RateLimitWhatsAppPerWindow", 20);

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

        // Suppress noisy channels once the user is over their per-channel rate
        // limit (storm control). Critical notifications always bypass so
        // deadlines can't be throttled. In-app is never rate-limited.
        if (RateLimitEnabled && priority != NotificationPriority.Critical)
        {
            channelDecision = await ApplyRateLimitsAsync(request.UserId, channelDecision, cancellationToken);
        }

        // If quiet hours and not critical, schedule for later
        if (channelDecision.IsQuietHours && priority != NotificationPriority.Critical && channelDecision.DeliveryTime.HasValue)
        {
            var scheduledId = await ScheduleAsync(request, channelDecision.DeliveryTime.Value, cancellationToken);
            _logger.LogInformation("Notification scheduled for {Time} due to quiet hours", channelDecision.DeliveryTime.Value);
            // NOTE: the returned id is a ScheduledNotification id, not a
            // Notification id. The "scheduled" delivery marker lets callers (and
            // ProcessScheduledNotificationsAsync) detect this and avoid treating
            // it as a Notification id (audit BE-27).
            return new SendNotificationResponse(scheduledId, [
                new DeliveryResultDto(ScheduledChannelMarker, "pending", null)
            ]);
        }

        // Deduplicate repeat events / job retries within the configured window
        // (audit BE-10). Off by default (window = 0).
        var referenceId = GetReferenceId(request.Data);
        if (DeduplicationWindowMinutes > 0 && referenceId.HasValue)
        {
            var since = DateTime.UtcNow.AddMinutes(-DeduplicationWindowMinutes);
            var duplicate = await _dbContext.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == request.UserId
                            && n.Type == request.Type
                            && n.ReferenceId == referenceId
                            && n.DeletedAt == null
                            && n.CreatedAt >= since)
                .OrderByDescending(n => n.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (duplicate != null)
            {
                _logger.LogInformation(
                    "Skipping duplicate notification (type {Type}, ref {Ref}) for user {UserId} within {Window}m window",
                    request.Type, referenceId, request.UserId, DeduplicationWindowMinutes);
                return new SendNotificationResponse(duplicate.Id, []);
            }
        }

        // Render notification content
        var rendered = await RenderNotificationContentAsync(
            request.Type, request.Data, ResolveLanguage(request.Data), cancellationToken);

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
            ReferenceId = referenceId,
            ReferenceType = GetReferenceType(request.Type),
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // BE-09: enqueue the channel fan-out instead of sending inline, so the
        // caller's request/scope isn't held for provider I/O and a crash mid-send
        // is resumable. The notification row is already committed, so the job can
        // reconstruct everything it needs from it.
        if (UseQueue)
        {
            _backgroundJobs.Enqueue<INotificationEngineService>(
                e => e.DispatchQueuedNotificationAsync(notification.Id, request.OverridePreferences, CancellationToken.None));

            _logger.LogInformation(
                "Queued notification {NotificationId} of type {Type} for user {UserId}",
                notification.Id, request.Type, request.UserId);

            return new SendNotificationResponse(notification.Id, [
                new DeliveryResultDto("queued", "queued", null)
            ]);
        }

        var deliveries = await DispatchChannelsAsync(
            notification, user, rendered, channelDecision, request.OverridePreferences, cancellationToken);

        _logger.LogInformation(
            "Sent notification {NotificationId} of type {Type} to user {UserId} via {ChannelCount} channels",
            notification.Id, request.Type, request.UserId, deliveries.Count);

        return new SendNotificationResponse(notification.Id, deliveries);
    }

    /// <summary>
    /// Downgrades a channel decision to respect per-user, per-channel rate limits.
    /// Counts the user's actually-sent deliveries in the rolling window and turns
    /// off any channel already at or over its cap. In-app is never limited, so
    /// the notification is always still recorded and visible in the centre.
    /// </summary>
    private async Task<ChannelDecision> ApplyRateLimitsAsync(
        Guid userId, ChannelDecision decision, CancellationToken cancellationToken)
    {
        // Nothing to limit if only in-app is active.
        if (!decision.ShouldSendEmail && !decision.ShouldSendSms
            && !decision.ShouldSendPush && !decision.ShouldSendWhatsApp)
        {
            return decision;
        }

        var windowStart = DateTime.UtcNow.AddMinutes(-RateLimitWindowMinutes);
        var counts = await _dbContext.NotificationDeliveries
            .Where(d => d.Notification.UserId == userId && d.SentAt >= windowStart)
            .GroupBy(d => d.Channel)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Channel, x => x.Count, cancellationToken);

        bool Over(string channel, int limit) => counts.GetValueOrDefault(channel) >= limit;

        var email = decision.ShouldSendEmail && !Over(NotificationChannel.Email, RateLimitEmail);
        var sms = decision.ShouldSendSms && !Over(NotificationChannel.Sms, RateLimitSms);
        var push = decision.ShouldSendPush && !Over(NotificationChannel.Push, RateLimitPush);
        var whatsApp = decision.ShouldSendWhatsApp && !Over(NotificationChannel.WhatsApp, RateLimitWhatsApp);

        if (email != decision.ShouldSendEmail || sms != decision.ShouldSendSms
            || push != decision.ShouldSendPush || whatsApp != decision.ShouldSendWhatsApp)
        {
            _logger.LogInformation(
                "Rate limit suppressed channels for user {UserId} (email:{E} sms:{S} push:{P} whatsapp:{W}); in-app preserved",
                userId, !email, !sms, !push, !whatsApp);
        }

        return decision with
        {
            ShouldSendEmail = email,
            ShouldSendSms = sms,
            ShouldSendPush = push,
            ShouldSendWhatsApp = whatsApp
        };
    }

    /// <summary>
    /// Runs the per-channel fan-out for a notification. Channels are sent
    /// SEQUENTIALLY because they share the scoped DbContext, which is not
    /// thread-safe (audit BE-02).
    /// </summary>
    private async Task<List<DeliveryResultDto>> DispatchChannelsAsync(
        Notification notification, ApplicationUser user, RenderedTemplate rendered,
        ChannelDecision channelDecision, bool overridePreferences, CancellationToken cancellationToken)
    {
        var priority = notification.Priority;
        var deliveries = new List<DeliveryResultDto>();

        if (channelDecision.ShouldSendEmail || overridePreferences)
        {
            deliveries.Add(await SendEmailAsync(notification, user, rendered, cancellationToken));
        }

        if (channelDecision.ShouldSendSms || (overridePreferences && priority == NotificationPriority.Critical))
        {
            deliveries.Add(await SendSmsAsync(notification, user, rendered, cancellationToken));
        }

        if (channelDecision.ShouldSendPush || overridePreferences)
        {
            deliveries.Add(await SendPushAsync(notification, user, notification.Data, rendered, cancellationToken));
        }

        if (channelDecision.ShouldSendWhatsApp || (overridePreferences && priority == NotificationPriority.Critical))
        {
            deliveries.Add(await SendWhatsAppAsync(notification, user, notification.Type, notification.Data, cancellationToken));
        }

        // In-app is always sent (unless explicitly disabled)
        if (channelDecision.ShouldSendInApp)
        {
            deliveries.Add(await SendInAppAsync(notification, user, cancellationToken));
        }

        return deliveries;
    }

    /// <inheritdoc />
    public async Task DispatchQueuedNotificationAsync(Guid notificationId, bool overridePreferences, CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

        if (notification == null)
        {
            _logger.LogWarning("Queued notification {NotificationId} not found; skipping", notificationId);
            return;
        }

        // Idempotency for job retries: if this notification already has delivery
        // rows, a prior run already dispatched it — don't re-send (audit BE-10).
        var alreadyDispatched = await _dbContext.NotificationDeliveries
            .AnyAsync(d => d.NotificationId == notificationId, cancellationToken);
        if (alreadyDispatched)
        {
            _logger.LogInformation("Notification {NotificationId} already dispatched; skipping duplicate job", notificationId);
            return;
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == notification.UserId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for queued notification {NotificationId}", notification.UserId, notificationId);
            return;
        }

        var channelDecision = await _preferencesService.EvaluateChannelsAsync(
            notification.UserId, notification.Type, notification.Priority, cancellationToken);

        // Content was already rendered and stored on the notification.
        var rendered = new RenderedTemplate(notification.Title, notification.Body, notification.Title);

        var deliveries = await DispatchChannelsAsync(
            notification, user, rendered, channelDecision, overridePreferences, cancellationToken);

        _logger.LogInformation(
            "Dispatched queued notification {NotificationId} via {ChannelCount} channels",
            notificationId, deliveries.Count);
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
    public async Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Owner-scoped soft delete.
        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId && n.DeletedAt == null, cancellationToken);

        if (notification == null)
            return false;

        notification.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Deleting an unread notification changes the unread count.
        if (!notification.IsRead)
        {
            var unreadCount = await GetUnreadCountAsync(userId, cancellationToken);
            await _inAppService.SendBadgeUpdateAsync(userId, unreadCount, cancellationToken);
        }

        return true;
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
            .OrderBy(s => s.ScheduledFor)  // oldest first — don't starve old rows (audit BE-28)
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

                // If the inner send re-entered quiet hours it created a NEW
                // ScheduledNotification and returned THAT id. That is not a
                // Notification, so it must not be written to the SentNotificationId
                // FK or SaveChanges throws and rolls back the whole batch (audit BE-27).
                var wasRescheduled = response.Deliveries.Any(d => d.Channel == ScheduledChannelMarker);

                scheduled.Status = "sent";
                scheduled.SentNotificationId = wasRescheduled ? null : response.NotificationId;

                _logger.LogInformation("Processed scheduled notification {Id}", scheduled.Id);
            }
            catch (KeyNotFoundException)
            {
                // Recipient no longer exists (e.g. user deleted since scheduling).
                // Terminal — mark failed so it stops retrying every cycle (audit BE-28).
                _logger.LogWarning("Scheduled notification {Id} failed permanently (recipient missing)", scheduled.Id);
                scheduled.Status = "failed";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process scheduled notification {Id}", scheduled.Id);
                scheduled.Status = "failed";
            }

            // Persist each item independently so one failure can't roll back the
            // others or block the whole batch (audit BE-27 / BE-28).
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist result for scheduled notification {Id}", scheduled.Id);
                // Drop only this entity's pending change so the loop can continue
                // and the other rows (still tracked) still persist their updates.
                _dbContext.Entry(scheduled).State = EntityState.Unchanged;
            }
        }
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
            .OrderBy(d => d.NextRetryAt)
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
                    delivery.NextRetryAt = null;
                    _logger.LogInformation("Retry successful for delivery {Id}", delivery.Id);
                }
                else
                {
                    delivery.RetryCount++;
                    delivery.FailureReason = result.ErrorMessage;
                    await HandleRetryFailureAsync(delivery, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying delivery {Id}", delivery.Id);
                delivery.RetryCount++;
                await HandleRetryFailureAsync(delivery, cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// After a retry attempt fails, either schedule the next retry or, once the
    /// channel's retry budget is exhausted, escalate the delivery to the dead
    /// letter queue so it stops being stranded in Failed forever (audit BE-04).
    /// </summary>
    private async Task HandleRetryFailureAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        delivery.FailedAt ??= DateTime.UtcNow;

        if (delivery.RetryCount >= GetMaxRetries(delivery.Channel))
        {
            try
            {
                var deadLetterService = _serviceProvider.GetRequiredService<IDeadLetterService>();
                var recipient = GetRecipientForChannel(delivery);
                await deadLetterService.MoveToDeadLetterAsync(delivery, recipient, cancellationToken);
                _logger.LogWarning(
                    "Delivery {Id} moved to dead letter after {Attempts} attempts",
                    delivery.Id, delivery.RetryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move delivery {Id} to dead letter", delivery.Id);
                // Stop retrying regardless so it does not loop forever.
                delivery.NextRetryAt = null;
            }
        }
        else
        {
            delivery.NextRetryAt = CalculateNextRetry(delivery.RetryCount, delivery.Channel);
            _logger.LogWarning("Retry failed for delivery {Id}, attempt {Attempt}", delivery.Id, delivery.RetryCount);
        }
    }

    private static string GetRecipientForChannel(NotificationDelivery delivery)
    {
        var user = delivery.Notification?.User;
        if (user == null)
            return "unknown";

        return delivery.Channel switch
        {
            NotificationChannel.Email => user.Email ?? "unknown",
            NotificationChannel.Sms or NotificationChannel.WhatsApp => user.Mobile ?? "unknown",
            _ => $"user:{user.Id}"
        };
    }

    #region Private Helper Methods

    private async Task<RenderedTemplate> RenderNotificationContentAsync(
        string type, Dictionary<string, object> data, string language, CancellationToken cancellationToken)
    {
        // Render the shared, channel-agnostic content from a DB template if one
        // exists (with language → English fallback), otherwise use built-in
        // content. TryRenderAsync returns null rather than throwing, so the
        // template path is real instead of exception-driven (audit BE-08).
        var rendered = await _templateService.TryRenderAsync(
            type, NotificationChannel.Default, data, language, cancellationToken);

        return rendered ?? GenerateDefaultContent(type, data);
    }

    private static string ResolveLanguage(Dictionary<string, object> data)
    {
        // No per-user language column yet; honour an explicit "language" hint in
        // the event data, else default to English (audit BE-08 — a stored user
        // language preference is a follow-up).
        if (data.TryGetValue("language", out var lang) && lang is not null)
        {
            var value = lang.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value!.ToLowerInvariant();
        }
        return "en";
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
            if (!result.Success)
            {
                delivery.FailedAt = DateTime.UtcNow;
                delivery.NextRetryAt = CalculateNextRetry(0, NotificationChannel.Sms);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.Sms, delivery.Status, result.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS for notification {NotificationId}", notification.Id);
            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureReason = ex.Message;
            delivery.FailedAt = DateTime.UtcNow;
            delivery.NextRetryAt = CalculateNextRetry(0, NotificationChannel.Sms);
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
                ["category"] = notification.Category,
                ["deepLink"] = $"effortlessinsight://notification/{notification.Id}",
            };

            // Only forward non-sensitive routing identifiers. Never copy business
            // data (gstin, demandAmount, clientName, errorMessage, ...): FCM/Expo
            // store and relay the payload and it is readable on-device. The client
            // fetches details in-app after tapping (audit BE-15).
            foreach (var key in PushDataAllowlist)
            {
                if (data.TryGetValue(key, out var value) && value != null)
                {
                    var stringValue = value.ToString();
                    if (!string.IsNullOrEmpty(stringValue))
                        pushData[key] = stringValue;
                }
            }

            if (notification.ActionUrl != null)
                pushData["actionUrl"] = notification.ActionUrl;

            // Server-driven badge so iOS reflects the unread count (audit BE-19).
            var unreadCount = await GetUnreadCountAsync(user.Id, cancellationToken);

            var isUrgent = notification.Priority == NotificationPriority.Critical
                || notification.Priority == NotificationPriority.High;

            var message = new PushNotificationMessage(
                rendered.Title,
                rendered.Body,
                pushData,
                Priority: isUrgent ? "high" : "normal",   // High also gets high priority (audit BE-32)
                ChannelId: GetAndroidChannelId(notification.Type),
                BadgeCount: unreadCount,
                DeepLink: $"effortlessinsight://notification/{notification.Id}",
                ActionUrl: notification.ActionUrl,
                TimeToLive: GetPushTtl(notification.Type),
                CollapseKey: notification.ReferenceId?.ToString(),
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

            // Refresh LastUsedAt for tokens that delivered, so the hygiene job can
            // distinguish live devices from stale ones (audit BE-21).
            var deliveredTokens = new List<string>();
            // Map Expo ticket ids to their tokens so the receipt poller can later
            // resolve DeviceNotRegistered back to a token (audit BE-25).
            var expoTickets = new Dictionary<string, string>();
            for (int i = 0; i < results.Count && i < tokenStrings.Count; i++)
            {
                if (!results[i].Success)
                    continue;
                deliveredTokens.Add(tokenStrings[i]);
                if (tokenStrings[i].StartsWith("ExponentPushToken[", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(results[i].MessageId))
                {
                    expoTickets[results[i].MessageId!] = tokenStrings[i];
                }
            }
            if (deliveredTokens.Count > 0)
                await _pushTokenService.TouchTokensAsync(deliveredTokens, cancellationToken);
            if (expoTickets.Count > 0)
            {
                delivery.Metadata["expoTickets"] = expoTickets;
                delivery.Metadata["expoReceiptsChecked"] = false;
            }

            var anySuccess = results.Any(r => r.Success);
            delivery.Status = anySuccess ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            delivery.ProviderMessageId = results.FirstOrDefault(r => r.Success)?.MessageId;
            delivery.SentAt = anySuccess ? DateTime.UtcNow : null;
            if (!anySuccess)
            {
                // Schedule a retry so the retry job actually picks this up; a
                // NULL NextRetryAt previously stranded push failures (audit BE-03).
                delivery.FailedAt = DateTime.UtcNow;
                delivery.FailureReason = results.FirstOrDefault(r => !r.Success)?.ErrorMessage ?? "All push tokens failed";
                delivery.NextRetryAt = CalculateNextRetry(0, NotificationChannel.Push);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.Push, delivery.Status, delivery.ProviderMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push for notification {NotificationId}", notification.Id);
            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureReason = ex.Message;
            delivery.FailedAt = DateTime.UtcNow;
            delivery.NextRetryAt = CalculateNextRetry(0, NotificationChannel.Push);
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
            if (!result.Success)
            {
                delivery.FailedAt = DateTime.UtcNow;
                delivery.NextRetryAt = CalculateNextRetry(0, NotificationChannel.WhatsApp);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return new DeliveryResultDto(NotificationChannel.WhatsApp, delivery.Status, result.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp for notification {NotificationId}", notification.Id);
            delivery.Status = DeliveryStatus.Failed;
            delivery.FailureReason = ex.Message;
            delivery.FailedAt = DateTime.UtcNow;
            delivery.NextRetryAt = CalculateNextRetry(0, NotificationChannel.WhatsApp);
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

        var tokenStrings = tokens.Select(t => t.Token).ToList();

        // Reuse the stored notification content instead of regenerating defaults,
        // and carry enough data for the client to route (audit BE-29).
        var message = new PushNotificationMessage(
            notification.Title,
            notification.Body,
            new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString(),
                ["type"] = notification.Type,
                ["category"] = notification.Category
            },
            ChannelId: GetAndroidChannelId(notification.Type),
            NotificationId: notification.Id.ToString());

        var results = await _pushService.SendToTokensAsync(message, tokenStrings, cancellationToken);

        // Deactivate tokens FCM/Expo report as unregistered, exactly as the
        // primary send path does (audit BE-29).
        for (int i = 0; i < results.Count && i < tokenStrings.Count; i++)
        {
            if (!results[i].Success && results[i].ErrorCode == "UNREGISTERED")
                await _pushTokenService.MarkTokenInvalidAsync(tokenStrings[i], cancellationToken);
        }

        // Success if ANY token delivered, not just the first.
        return results.FirstOrDefault(r => r.Success)
            ?? results.FirstOrDefault()
            ?? new ChannelSendResult(false, null, "NO_RESULT", "No result from push service");
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

    // Routing identifiers that are safe to place in a push payload. Excludes all
    // business/PII fields (audit BE-15).
    private static readonly string[] PushDataAllowlist =
    {
        "noticeId", "taskId", "commentId", "workflowId", "documentId", "referenceId"
    };

    private static TimeSpan? GetPushTtl(string type) => type switch
    {
        NotificationType.DeadlineToday or NotificationType.DeadlineMissed => TimeSpan.FromHours(12),
        NotificationType.Deadline1Day => TimeSpan.FromHours(24),
        NotificationType.Deadline3Day or NotificationType.Deadline7Day => TimeSpan.FromDays(3),
        _ => null // FCM default (4 weeks)
    };

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

        // If the one-click unsubscribe token cannot be generated (secret not
        // configured), fall back to the preferences page so the email footer
        // stays valid instead of the whole email build throwing (audit BE-30).
        var unsubscribeToken = GenerateUnsubscribeToken(user.Id);
        var unsubscribeUrl = unsubscribeToken != null
            ? $"https://app.effortlessinsight.com/unsubscribe?token={unsubscribeToken}"
            : "https://app.effortlessinsight.com/settings/notifications";

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
        <a href=""{unsubscribeUrl}"" style=""color: #1e40af;"">Unsubscribe</a>
      </p>
      <p>© {DateTime.UtcNow.Year} EffortlessInsight. All rights reserved.</p>
    </div>
  </div>
</body>
</html>";
    }

    /// <summary>
    /// Resolves the secret used to sign unsubscribe tokens. Prefers a dedicated
    /// secret so it is not coupled to the JWT signing key (which may be an RSA
    /// key pair with no symmetric secret at all). Falls back to the JWT symmetric
    /// secret, then the legacy "Jwt:Key" name for backward compatibility.
    /// </summary>
    private string? GetUnsubscribeSecret()
    {
        var secret = _configuration["Notifications:UnsubscribeSecret"]
            ?? _configuration["Jwt:Secret"]
            ?? _configuration["Jwt:Key"];
        return string.IsNullOrEmpty(secret) ? null : secret;
    }

    private string? GenerateUnsubscribeToken(Guid userId)
    {
        // Use HMAC-SHA256 to create a signed, non-guessable token. If no secret
        // is configured, degrade gracefully (return null) rather than throwing,
        // so a missing key cannot break every outgoing email (audit BE-30).
        var secret = GetUnsubscribeSecret();
        if (secret == null)
        {
            _logger.LogWarning(
                "Unsubscribe secret not configured (set Notifications:UnsubscribeSecret or Jwt:Secret); " +
                "omitting one-click unsubscribe link from notification email");
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = $"{userId}:{timestamp}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signatureBase64 = Convert.ToBase64String(signature);

        // Format: base64(userId:timestamp):signature
        var tokenData = $"{Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))}:{signatureBase64}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenData));
    }

    /// <summary>
    /// Validates an unsubscribe token and returns the user ID if valid.
    /// Tokens are valid for 30 days.
    /// </summary>
    public (bool IsValid, Guid? UserId) ValidateUnsubscribeToken(string token)
    {
        try
        {
            var tokenData = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = tokenData.Split(':');
            if (parts.Length != 2) return (false, null);

            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            var providedSignature = parts[1];

            var payloadParts = payload.Split(':');
            if (payloadParts.Length != 2) return (false, null);

            if (!Guid.TryParse(payloadParts[0], out var userId)) return (false, null);
            if (!long.TryParse(payloadParts[1], out var timestamp)) return (false, null);

            // Verify signature using the same resolution as generation
            var secret = GetUnsubscribeSecret();
            if (secret == null) return (false, null);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expectedSignature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedSignature),
                Encoding.UTF8.GetBytes(expectedSignature)))
            {
                return (false, null);
            }

            // Check expiration (30 days)
            var tokenTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            if (DateTimeOffset.UtcNow - tokenTime > TimeSpan.FromDays(30))
            {
                return (false, null);
            }

            return (true, userId);
        }
        catch
        {
            return (false, null);
        }
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
