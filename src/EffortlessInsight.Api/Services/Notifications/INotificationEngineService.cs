using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Core notification engine responsible for dispatching notifications across channels
/// </summary>
public interface INotificationEngineService
{
    /// <summary>
    /// Send a notification to a user, respecting preferences and quiet hours
    /// </summary>
    Task<SendNotificationResponse> SendAsync(SendNotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send bulk notifications (for scheduled jobs)
    /// </summary>
    Task<BulkNotificationResponse> SendBulkAsync(BulkNotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get notifications for a user with filtering and pagination
    /// </summary>
    Task<NotificationListResponse> GetUserNotificationsAsync(
        Guid userId,
        string? status = null,
        string? type = null,
        DateTime? since = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get unread notification count for a user
    /// </summary>
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a notification as read
    /// </summary>
    Task<MarkReadResponse> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark all notifications as read for a user
    /// </summary>
    Task<MarkAllReadResponse> MarkAllAsReadAsync(Guid userId, MarkAllReadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule a notification for future delivery
    /// </summary>
    Task<Guid> ScheduleAsync(SendNotificationRequest request, DateTime scheduledFor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a scheduled notification
    /// </summary>
    Task<bool> CancelScheduledAsync(Guid scheduledNotificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process scheduled notifications that are due
    /// </summary>
    Task ProcessScheduledNotificationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retry failed deliveries
    /// </summary>
    Task ProcessFailedDeliveriesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// User notification preferences service
/// </summary>
public interface INotificationPreferencesService
{
    /// <summary>
    /// Get user's notification preferences
    /// </summary>
    Task<NotificationPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update user's notification preferences
    /// </summary>
    Task<NotificationPreferencesDto> UpdatePreferencesAsync(Guid userId, UpdatePreferencesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initialize default preferences for a new user
    /// </summary>
    Task InitializeDefaultPreferencesAsync(Guid userId, string email, string? phone = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if notification should be sent based on user preferences
    /// </summary>
    Task<ChannelDecision> EvaluateChannelsAsync(Guid userId, string notificationType, string priority, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process email unsubscribe
    /// </summary>
    Task<UnsubscribeResponse> UnsubscribeAsync(string email, string? notificationType, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if email is unsubscribed
    /// </summary>
    Task<bool> IsUnsubscribedAsync(string email, string? notificationType = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of channel evaluation for a notification
/// </summary>
public record ChannelDecision(
    bool ShouldSendEmail,
    bool ShouldSendSms,
    bool ShouldSendPush,
    bool ShouldSendWhatsApp,
    bool ShouldSendInApp,
    bool IsQuietHours,
    bool DelayedUntil,
    DateTime? DeliveryTime
);

/// <summary>
/// Push token management service
/// </summary>
public interface IPushTokenService
{
    /// <summary>
    /// Register or update a push token
    /// </summary>
    Task<PushTokenDto> RegisterTokenAsync(Guid userId, RegisterPushTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active tokens for a user
    /// </summary>
    Task<List<PushToken>> GetActiveTokensAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivate a push token (e.g., on logout)
    /// </summary>
    Task DeactivateTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark token as invalid (after Firebase returns invalid token error)
    /// </summary>
    Task MarkTokenInvalidAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update last used timestamp
    /// </summary>
    Task UpdateLastUsedAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleanup old inactive tokens
    /// </summary>
    Task CleanupInactiveTokensAsync(int daysOld = 90, CancellationToken cancellationToken = default);
}

/// <summary>
/// Notification template service
/// </summary>
public interface INotificationTemplateService
{
    /// <summary>
    /// Get template for a notification type, channel, and language
    /// </summary>
    Task<NotificationTemplate?> GetTemplateAsync(string type, string channel, string language = "en", CancellationToken cancellationToken = default);

    /// <summary>
    /// Render template with variables
    /// </summary>
    Task<RenderedTemplate> RenderAsync(string type, string channel, Dictionary<string, object> variables, string language = "en", CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or update a template
    /// </summary>
    Task<NotificationTemplateDto> UpsertTemplateAsync(UpsertTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all templates
    /// </summary>
    Task<List<NotificationTemplateDto>> GetAllTemplatesAsync(string? type = null, string? channel = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seed default templates
    /// </summary>
    Task SeedDefaultTemplatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Rendered template result
/// </summary>
public record RenderedTemplate(
    string? Subject,
    string Body,
    string Title
);

/// <summary>
/// Delivery status tracking service
/// </summary>
public interface IDeliveryTrackingService
{
    /// <summary>
    /// Update delivery status from webhook
    /// </summary>
    Task UpdateStatusAsync(string channel, string messageId, string status, DateTime timestamp, string? errorReason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get delivery statistics
    /// </summary>
    Task<NotificationMetricsDto> GetMetricsAsync(Guid? organizationId = null, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record click event
    /// </summary>
    Task RecordClickAsync(string channel, string messageId, string? url = null, CancellationToken cancellationToken = default);
}
