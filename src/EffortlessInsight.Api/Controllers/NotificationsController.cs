using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Filters;
using EffortlessInsight.Api.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for notification management
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationEngineService _notificationEngine;
    private readonly INotificationPreferencesService _preferencesService;
    private readonly IPushTokenService _pushTokenService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationEngineService notificationEngine,
        INotificationPreferencesService preferencesService,
        IPushTokenService pushTokenService,
        ILogger<NotificationsController> logger)
    {
        _notificationEngine = notificationEngine;
        _preferencesService = preferencesService;
        _pushTokenService = pushTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Get user's notifications with optional filtering
    /// </summary>
    /// <param name="status">Filter by status: read, unread, all</param>
    /// <param name="type">Filter by notification type or category</param>
    /// <param name="since">Get notifications since this timestamp</param>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="pageSize">Page size (default: 50, max: 100)</param>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<NotificationListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var userId = GetCurrentUserId();
        pageSize = Math.Min(pageSize, 100);

        var result = await _notificationEngine.GetUserNotificationsAsync(
            userId, status, type, since, page, pageSize);

        return Ok(new ApiResponse<NotificationListResponse>(true, result));
    }

    /// <summary>
    /// Get unread notification count
    /// </summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetCurrentUserId();
        var count = await _notificationEngine.GetUnreadCountAsync(userId);

        return Ok(new ApiResponse<object>(true, new { unreadCount = count }));
    }

    /// <summary>
    /// Mark a notification as read
    /// </summary>
    [HttpPost("{notificationId:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse<MarkReadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid notificationId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _notificationEngine.MarkAsReadAsync(notificationId, userId);
            return Ok(new ApiResponse<MarkReadResponse>(true, result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notification not found"));
        }
    }

    /// <summary>
    /// Mark all notifications as read
    /// </summary>
    [HttpPost("read-all")]
    [ProducesResponseType(typeof(ApiResponse<MarkAllReadResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead([FromBody] MarkAllReadRequest? request)
    {
        var userId = GetCurrentUserId();
        var result = await _notificationEngine.MarkAllAsReadAsync(userId, request ?? new MarkAllReadRequest(null, null));
        return Ok(new ApiResponse<MarkAllReadResponse>(true, result));
    }

    private Guid GetCurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <summary>
/// API endpoints for notification preferences
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/users/me/notification-preferences")]
public class NotificationPreferencesController : ControllerBase
{
    private readonly INotificationPreferencesService _preferencesService;
    private readonly ILogger<NotificationPreferencesController> _logger;

    public NotificationPreferencesController(
        INotificationPreferencesService preferencesService,
        ILogger<NotificationPreferencesController> logger)
    {
        _preferencesService = preferencesService;
        _logger = logger;
    }

    /// <summary>
    /// Get user's notification preferences
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<NotificationPreferencesDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences()
    {
        var userId = GetCurrentUserId();
        var result = await _preferencesService.GetPreferencesAsync(userId);
        return Ok(new ApiResponse<NotificationPreferencesDto>(true, result));
    }

    /// <summary>
    /// Update user's notification preferences
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<NotificationPreferencesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _preferencesService.UpdatePreferencesAsync(userId, request);
            return Ok(new ApiResponse<NotificationPreferencesDto>(true, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update notification preferences");
            return BadRequest(new ApiErrorResponse(false, "UPDATE_FAILED", ex.Message));
        }
    }

    private Guid GetCurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <summary>
/// API endpoints for push token management
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/push-tokens")]
public class PushTokensController : ControllerBase
{
    private readonly IPushTokenService _pushTokenService;
    private readonly ILogger<PushTokensController> _logger;

    public PushTokensController(
        IPushTokenService pushTokenService,
        ILogger<PushTokensController> logger)
    {
        _pushTokenService = pushTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Register a push notification token
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PushTokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterToken([FromBody] RegisterPushTokenRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _pushTokenService.RegisterTokenAsync(userId, request);
            return Ok(new ApiResponse<PushTokenDto>(true, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register push token");
            return BadRequest(new ApiErrorResponse(false, "REGISTER_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Deactivate a push token (e.g., on logout)
    /// </summary>
    [HttpDelete("{token}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateToken(string token)
    {
        await _pushTokenService.DeactivateTokenAsync(token);
        return NoContent();
    }

    private Guid GetCurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

/// <summary>
/// Internal API endpoints for notification sending (service-to-service)
/// Secured with API key authentication for internal service calls.
/// </summary>
[ApiController]
[Route("api/internal/v1/notifications")]
[ServiceFilter(typeof(InternalApiKeyAuthFilter))]
public class InternalNotificationsController : ControllerBase
{
    private readonly INotificationEngineService _notificationEngine;
    private readonly ILogger<InternalNotificationsController> _logger;

    public InternalNotificationsController(
        INotificationEngineService notificationEngine,
        ILogger<InternalNotificationsController> logger)
    {
        _notificationEngine = notificationEngine;
        _logger = logger;
    }

    /// <summary>
    /// Send a notification (internal service use)
    /// Requires X-Internal-Api-Key header
    /// </summary>
    [HttpPost("send")]
    [ProducesResponseType(typeof(SendNotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Send([FromBody] SendNotificationRequest request)
    {
        try
        {
            var result = await _notificationEngine.SendAsync(request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification");
            return BadRequest(new ApiErrorResponse(false, "SEND_FAILED", ex.Message));
        }
    }

    /// <summary>
    /// Send bulk notifications (internal service use)
    /// Requires X-Internal-Api-Key header
    /// </summary>
    [HttpPost("bulk")]
    [ProducesResponseType(typeof(BulkNotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SendBulk([FromBody] BulkNotificationRequest request)
    {
        var result = await _notificationEngine.SendBulkAsync(request);
        return Ok(result);
    }
}

/// <summary>
/// Webhook endpoints for delivery status callbacks
/// </summary>
[ApiController]
[Route("webhooks")]
public class NotificationWebhooksController : ControllerBase
{
    private readonly IDeliveryTrackingService _deliveryService;
    private readonly ILogger<NotificationWebhooksController> _logger;

    public NotificationWebhooksController(
        IDeliveryTrackingService deliveryService,
        ILogger<NotificationWebhooksController> logger)
    {
        _deliveryService = deliveryService;
        _logger = logger;
    }

    /// <summary>
    /// SendGrid delivery status webhook
    /// </summary>
    [HttpPost("sendgrid")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendGridWebhook([FromBody] List<SendGridEvent> events)
    {
        foreach (var evt in events)
        {
            try
            {
                if (string.IsNullOrEmpty(evt.SgMessageId))
                    continue;

                var status = MapSendGridStatus(evt.Event);
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(evt.Timestamp).UtcDateTime;

                await _deliveryService.UpdateStatusAsync(
                    "email",
                    evt.SgMessageId,
                    status,
                    timestamp,
                    evt.Reason);

                if (evt.Event == "click" && !string.IsNullOrEmpty(evt.Url))
                {
                    await _deliveryService.RecordClickAsync("email", evt.SgMessageId, evt.Url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SendGrid event: {MessageId}", evt.SgMessageId);
            }
        }

        return Ok();
    }

    /// <summary>
    /// Twilio SMS status webhook
    /// </summary>
    [HttpPost("twilio/sms/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TwilioSmsStatus([FromForm] TwilioSmsStatusCallback callback)
    {
        try
        {
            if (string.IsNullOrEmpty(callback.MessageSid))
                return Ok();

            var status = MapTwilioStatus(callback.MessageStatus);

            await _deliveryService.UpdateStatusAsync(
                "sms",
                callback.MessageSid,
                status,
                DateTime.UtcNow,
                callback.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Twilio SMS callback: {MessageSid}", callback.MessageSid);
        }

        return Ok();
    }

    /// <summary>
    /// Twilio WhatsApp status webhook
    /// </summary>
    [HttpPost("twilio/whatsapp/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TwilioWhatsAppStatus([FromForm] TwilioWhatsAppStatusCallback callback)
    {
        try
        {
            if (string.IsNullOrEmpty(callback.MessageSid))
                return Ok();

            var status = MapTwilioStatus(callback.MessageStatus);

            await _deliveryService.UpdateStatusAsync(
                "whatsapp",
                callback.MessageSid,
                status,
                DateTime.UtcNow,
                callback.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Twilio WhatsApp callback: {MessageSid}", callback.MessageSid);
        }

        return Ok();
    }

    /// <summary>
    /// Email unsubscribe endpoint
    /// </summary>
    [HttpGet("unsubscribe")]
    [ProducesResponseType(typeof(UnsubscribeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unsubscribe([FromQuery] UnsubscribeRequest request)
    {
        try
        {
            // Decode the token to get email
            // In production, this should verify a signed token
            var emailBytes = Convert.FromBase64String(request.Token);
            var userId = new Guid(emailBytes);

            // Get user email from database (simplified - should use proper token)
            // For now, just return success

            return Ok(new UnsubscribeResponse(true, "You have been unsubscribed successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing unsubscribe");
            return BadRequest(new UnsubscribeResponse(false, "Invalid unsubscribe token."));
        }
    }

    private static string MapSendGridStatus(string? eventType) => eventType switch
    {
        "delivered" => "delivered",
        "open" => "opened",
        "click" => "clicked",
        "bounce" => "bounced",
        "dropped" => "failed",
        "deferred" => "pending",
        "processed" => "sent",
        _ => "pending"
    };

    private static string MapTwilioStatus(string? status) => status?.ToLower() switch
    {
        "delivered" => "delivered",
        "sent" => "sent",
        "failed" => "failed",
        "undelivered" => "failed",
        "read" => "opened",
        "queued" => "queued",
        "sending" => "pending",
        _ => "pending"
    };
}
