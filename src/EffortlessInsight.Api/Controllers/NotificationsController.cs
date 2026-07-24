using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Filters;
using EffortlessInsight.Api.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Delete (soft-delete) a notification. Used by the web and mobile
    /// notification centres, whose delete calls previously hit a missing route.
    /// </summary>
    [HttpDelete("{notificationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNotification(Guid notificationId)
    {
        var userId = GetCurrentUserId();
        var deleted = await _notificationEngine.DeleteNotificationAsync(notificationId, userId);
        return deleted
            ? NoContent()
            : NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notification not found"));
    }

    private Guid GetCurrentUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);
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
        Guid.Parse(User.FindFirstValue("sub")!);
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
        // Scoped to the current user so one user cannot deactivate another's
        // token by value (audit BE-13). Always returns 204 so the endpoint does
        // not reveal whether a token exists.
        var userId = GetCurrentUserId();
        await _pushTokenService.DeactivateTokenAsync(token, userId);
        return NoContent();
    }

    private Guid GetCurrentUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);
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
    private readonly INotificationEngineService _notificationEngine;
    private readonly INotificationPreferencesService _preferencesService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationWebhooksController> _logger;

    private static readonly JsonSerializerOptions WebhookJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NotificationWebhooksController(
        IDeliveryTrackingService deliveryService,
        INotificationEngineService notificationEngine,
        INotificationPreferencesService preferencesService,
        IConfiguration configuration,
        ILogger<NotificationWebhooksController> logger)
    {
        _deliveryService = deliveryService;
        _notificationEngine = notificationEngine;
        _preferencesService = preferencesService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Resend email delivery/open/click/bounce webhook (audit BE-25). Feeds the
    /// delivery tracking service so DeliveredAt/OpenedAt/ClickedAt and the
    /// metrics dashboard are populated.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("resend")]
    public async Task<IActionResult> ResendWebhook(CancellationToken cancellationToken)
    {
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        // Verify the Svix signature when a secret is configured; reject on
        // mismatch. If no secret is set, accept but warn (webhook not yet locked
        // down) rather than silently dropping events.
        var secret = _configuration["Resend:WebhookSecret"];
        if (!string.IsNullOrEmpty(secret))
        {
            if (!VerifySvixSignature(secret, Request.Headers, body))
            {
                _logger.LogWarning("Resend webhook signature verification failed");
                return Unauthorized();
            }
        }
        else
        {
            _logger.LogWarning("Resend webhook secret not configured; accepting event unverified");
        }

        ResendWebhookEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<ResendWebhookEvent>(body, WebhookJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed Resend webhook payload");
            return BadRequest();
        }

        var messageId = evt?.Data?.EmailId;
        if (evt == null || string.IsNullOrEmpty(messageId))
            return Ok(); // nothing actionable

        var timestamp = evt.CreatedAt ?? DateTime.UtcNow;

        switch (evt.Type)
        {
            case "email.delivered":
                await _deliveryService.UpdateStatusAsync(NotificationChannel.Email, messageId, DeliveryStatus.Delivered, timestamp, cancellationToken: cancellationToken);
                break;
            case "email.opened":
                await _deliveryService.UpdateStatusAsync(NotificationChannel.Email, messageId, DeliveryStatus.Opened, timestamp, cancellationToken: cancellationToken);
                break;
            case "email.clicked":
                await _deliveryService.RecordClickAsync(NotificationChannel.Email, messageId, evt.Data?.Click?.Link, cancellationToken);
                break;
            case "email.bounced":
                await _deliveryService.UpdateStatusAsync(NotificationChannel.Email, messageId, DeliveryStatus.Bounced, timestamp, "bounced", cancellationToken);
                break;
            case "email.complained":
                await _deliveryService.UpdateStatusAsync(NotificationChannel.Email, messageId, DeliveryStatus.Failed, timestamp, "spam_complaint", cancellationToken);
                break;
            default:
                _logger.LogDebug("Ignoring Resend event {Type}", evt.Type);
                break;
        }

        return Ok();
    }

    /// <summary>
    /// Verifies a Svix-style webhook signature (used by Resend). Signs
    /// "{id}.{timestamp}.{body}" with the base64 secret and compares against the
    /// space-separated v1 signatures in the svix-signature header.
    /// </summary>
    private static bool VerifySvixSignature(string secret, IHeaderDictionary headers, string body)
    {
        var id = headers["svix-id"].ToString();
        var timestamp = headers["svix-timestamp"].ToString();
        var signatureHeader = headers["svix-signature"].ToString();

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signatureHeader))
            return false;

        try
        {
            var key = secret.StartsWith("whsec_", StringComparison.Ordinal)
                ? Convert.FromBase64String(secret["whsec_".Length..])
                : Encoding.UTF8.GetBytes(secret);

            var signedContent = $"{id}.{timestamp}.{body}";
            using var hmac = new HMACSHA256(key);
            var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedContent)));

            // Header is like "v1,<sig> v1,<sig2>"; any match is acceptable.
            foreach (var part in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var sig = part.Contains(',') ? part[(part.IndexOf(',') + 1)..] : part;
                if (CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(sig), Encoding.UTF8.GetBytes(expected)))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private sealed record ResendWebhookEvent
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; init; }
        [JsonPropertyName("data")] public ResendWebhookData? Data { get; init; }
    }

    private sealed record ResendWebhookData
    {
        [JsonPropertyName("email_id")] public string? EmailId { get; init; }
        [JsonPropertyName("click")] public ResendWebhookClick? Click { get; init; }
    }

    private sealed record ResendWebhookClick
    {
        [JsonPropertyName("link")] public string? Link { get; init; }
    }

    /// <summary>
    /// Email unsubscribe endpoint
    /// </summary>
    [AllowAnonymous]
    [HttpGet("unsubscribe")]
    [ProducesResponseType(typeof(UnsubscribeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UnsubscribeResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Unsubscribe([FromQuery] DTOs.UnsubscribeRequest request)
    {
        try
        {
            // Validate the signed unsubscribe token
            var (isValid, userId) = _notificationEngine.ValidateUnsubscribeToken(request.Token);

            if (!isValid || !userId.HasValue)
            {
                _logger.LogWarning("Invalid or expired unsubscribe token attempted");
                return BadRequest(new UnsubscribeResponse(false, "Invalid or expired unsubscribe token."));
            }

            // Disable email notifications for this user
            await _preferencesService.UpdatePreferencesAsync(
                userId.Value,
                new UpdatePreferencesRequest
                {
                    Channels = new UpdateChannelPreferencesDto
                    {
                        Email = new UpdateEmailChannelDto { Enabled = false }
                    }
                });

            _logger.LogInformation("User {UserId} unsubscribed from email notifications", userId.Value);
            return Ok(new UnsubscribeResponse(true, "You have been unsubscribed from email notifications successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing unsubscribe");
            return BadRequest(new UnsubscribeResponse(false, "An error occurred while processing your request."));
        }
    }
}
