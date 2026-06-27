using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Resend;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using FcmNotification = FirebaseAdmin.Messaging.Notification;

namespace EffortlessInsight.Api.Services.Notifications;

#region Email Channel Service (Resend)

/// <summary>
/// Resend email channel service implementation
/// </summary>
public class ResendEmailService : IEmailChannelService
{
    private readonly IResend _resend;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IResend resend,
        IOptions<ResendOptions> options,
        ILogger<ResendEmailService> logger)
    {
        _resend = resend;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendAsync(EmailNotificationMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var emailMessage = new EmailMessage
            {
                From = $"{_options.FromName} <{_options.FromEmail}>",
                To = [message.ToEmail],
                Subject = message.Subject,
                HtmlBody = message.HtmlBody,
                TextBody = message.TextBody
            };

            if (!string.IsNullOrEmpty(_options.ReplyTo))
            {
                emailMessage.ReplyTo = _options.ReplyTo;
            }

            // Add attachments (base64 encoded)
            if (message.Attachments != null && message.Attachments.Count > 0)
            {
                foreach (var attachment in message.Attachments)
                {
                    emailMessage.Attachments.Add(new Resend.EmailAttachment
                    {
                        Filename = attachment.FileName,
                        Content = Convert.ToBase64String(attachment.Content)
                    });
                }
            }

            // Add headers for tracking
            if (!string.IsNullOrEmpty(message.NotificationId))
            {
                emailMessage.Headers.Add("X-Notification-Id", message.NotificationId);
            }
            if (!string.IsNullOrEmpty(message.UserId))
            {
                emailMessage.Headers.Add("X-User-Id", message.UserId);
            }

            var response = await _resend.EmailSendAsync(emailMessage, cancellationToken);

            if (response.Success)
            {
                var messageId = response.Content.ToString();
                _logger.LogInformation("Email sent successfully to {Email}, MessageId: {MessageId}",
                    message.ToEmail, messageId);
                return new ChannelSendResult(true, messageId, null, null);
            }

            var errorMessage = response.Exception?.Message ?? "Unknown error";
            _logger.LogError("Resend error: {Error}", errorMessage);
            return new ChannelSendResult(false, null, "RESEND_ERROR", errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", message.ToEmail);
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Send a simple API call to verify credentials by fetching domains
            var response = await _resend.DomainListAsync(cancellationToken);
            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend configuration verification failed");
            return false;
        }
    }
}

#endregion

#region SMS Channel Service (Twilio)

/// <summary>
/// Twilio SMS channel service implementation
/// </summary>
public class TwilioSmsService : ISmsChannelService
{
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsService> _logger;
    private bool _initialized;

    public TwilioSmsService(
        IOptions<TwilioOptions> options,
        ILogger<TwilioSmsService> logger)
    {
        _options = options.Value;
        _logger = logger;
        InitializeTwilio();
    }

    private void InitializeTwilio()
    {
        if (_initialized || string.IsNullOrEmpty(_options.AccountSid))
            return;

        TwilioClient.Init(_options.AccountSid, _options.AuthToken);
        _initialized = true;
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendAsync(SmsNotificationMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeTwilio();

            var formattedPhone = FormatPhoneNumber(message.ToPhone);

            var createOptions = new CreateMessageOptions(new PhoneNumber(formattedPhone))
            {
                Body = message.Body
            };

            // Use messaging service if configured, otherwise use from number
            if (!string.IsNullOrEmpty(_options.MessagingServiceSid))
            {
                createOptions.MessagingServiceSid = _options.MessagingServiceSid;
            }
            else
            {
                createOptions.From = new PhoneNumber(_options.SmsFromNumber);
            }

            // Set up status callback
            if (!string.IsNullOrEmpty(_options.WebhookBaseUrl))
            {
                createOptions.StatusCallback = new Uri($"{_options.WebhookBaseUrl}/webhooks/twilio/sms/status");
            }

            var twilioMessage = await MessageResource.CreateAsync(createOptions);

            if (twilioMessage.Status == MessageResource.StatusEnum.Failed ||
                twilioMessage.Status == MessageResource.StatusEnum.Undelivered)
            {
                _logger.LogError("SMS send failed: {ErrorCode} - {ErrorMessage}",
                    twilioMessage.ErrorCode, twilioMessage.ErrorMessage);
                return new ChannelSendResult(false, twilioMessage.Sid, twilioMessage.ErrorCode?.ToString(), twilioMessage.ErrorMessage);
            }

            _logger.LogInformation("SMS sent successfully to {Phone}, SID: {Sid}", formattedPhone, twilioMessage.Sid);
            return new ChannelSendResult(true, twilioMessage.Sid, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {Phone}", message.ToPhone);
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendOtpAsync(string phone, string code, int expiryMinutes = 10, CancellationToken cancellationToken = default)
    {
        var body = $"Your EffortlessInsight OTP is {code}. Valid for {expiryMinutes} minutes. Do not share this code.";
        return await SendAsync(new SmsNotificationMessage(phone, body, null, true), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeTwilio();
            var account = await Twilio.Rest.Api.V2010.AccountResource.FetchAsync(_options.AccountSid);
            return account != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio configuration verification failed");
            return false;
        }
    }

    /// <inheritdoc />
    public string FormatPhoneNumber(string phone)
    {
        // Remove any non-digit characters
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // Indian phone numbers
        if (digits.Length == 10)
        {
            return $"+91{digits}";
        }

        // Already has country code
        if (digits.Length == 12 && digits.StartsWith("91"))
        {
            return $"+{digits}";
        }

        // Already formatted
        if (phone.StartsWith("+"))
        {
            return phone;
        }

        return $"+{digits}";
    }
}

#endregion

#region WhatsApp Channel Service (Twilio)

/// <summary>
/// Twilio WhatsApp channel service implementation with conversation tracking.
/// Tracks the 24-hour conversation window for WhatsApp Business API compliance.
/// </summary>
public class TwilioWhatsAppService : IWhatsAppChannelService
{
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioWhatsAppService> _logger;
    private readonly IDistributedCache _cache;
    private bool _initialized;

    // WhatsApp conversation window is 24 hours
    private const string ConversationKeyPrefix = "whatsapp:conversation:";
    private static readonly TimeSpan ConversationWindowDuration = TimeSpan.FromHours(24);

    public TwilioWhatsAppService(
        IOptions<TwilioOptions> options,
        ILogger<TwilioWhatsAppService> logger,
        IDistributedCache cache)
    {
        _options = options.Value;
        _logger = logger;
        _cache = cache;
        InitializeTwilio();
    }

    private void InitializeTwilio()
    {
        if (_initialized || string.IsNullOrEmpty(_options.AccountSid))
            return;

        TwilioClient.Init(_options.AccountSid, _options.AuthToken);
        _initialized = true;
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeTwilio();

            var formattedPhone = FormatWhatsAppNumber(message.ToPhone);

            // For WhatsApp templates, we need to use content templates
            var createOptions = new CreateMessageOptions(new PhoneNumber(formattedPhone))
            {
                From = new PhoneNumber(_options.WhatsAppFromNumber),
                ContentSid = message.TemplateSid
            };

            // Add template variables
            if (message.Variables.Any())
            {
                createOptions.ContentVariables = System.Text.Json.JsonSerializer.Serialize(message.Variables);
            }

            // Set up status callback
            if (!string.IsNullOrEmpty(_options.WebhookBaseUrl))
            {
                createOptions.StatusCallback = new Uri($"{_options.WebhookBaseUrl}/webhooks/twilio/whatsapp/status");
            }

            var twilioMessage = await MessageResource.CreateAsync(createOptions);

            if (twilioMessage.Status == MessageResource.StatusEnum.Failed ||
                twilioMessage.Status == MessageResource.StatusEnum.Undelivered)
            {
                _logger.LogError("WhatsApp send failed: {ErrorCode} - {ErrorMessage}",
                    twilioMessage.ErrorCode, twilioMessage.ErrorMessage);
                return new ChannelSendResult(false, twilioMessage.Sid, twilioMessage.ErrorCode?.ToString(), twilioMessage.ErrorMessage);
            }

            _logger.LogInformation("WhatsApp sent successfully to {Phone}, SID: {Sid}", formattedPhone, twilioMessage.Sid);
            return new ChannelSendResult(true, twilioMessage.Sid, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp to {Phone}", message.ToPhone);
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendTextAsync(string toPhone, string text, string? notificationId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeTwilio();

            var formattedPhone = FormatWhatsAppNumber(toPhone);

            var createOptions = new CreateMessageOptions(new PhoneNumber(formattedPhone))
            {
                From = new PhoneNumber(_options.WhatsAppFromNumber),
                Body = text
            };

            var twilioMessage = await MessageResource.CreateAsync(createOptions);

            return twilioMessage.Status == MessageResource.StatusEnum.Failed
                ? new ChannelSendResult(false, twilioMessage.Sid, twilioMessage.ErrorCode?.ToString(), twilioMessage.ErrorMessage)
                : new ChannelSendResult(true, twilioMessage.Sid, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp text to {Phone}", toPhone);
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsConversationActiveAsync(string phone, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPhone = NormalizePhoneForCache(phone);
            var cacheKey = $"{ConversationKeyPrefix}{normalizedPhone}";
            var lastInteraction = await _cache.GetStringAsync(cacheKey, cancellationToken);

            if (string.IsNullOrEmpty(lastInteraction))
            {
                return false;
            }

            // Check if within 24-hour window
            if (DateTime.TryParse(lastInteraction, out var interactionTime))
            {
                var isActive = DateTime.UtcNow - interactionTime < ConversationWindowDuration;
                _logger.LogDebug(
                    "WhatsApp conversation check for {Phone}: active={IsActive}, lastInteraction={LastInteraction}",
                    normalizedPhone, isActive, interactionTime);
                return isActive;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check WhatsApp conversation status for {Phone}", phone);
            // Return true to allow template messages as fallback
            return true;
        }
    }

    /// <summary>
    /// Records user interaction to start/extend the 24-hour conversation window.
    /// Call this when receiving an incoming message from the user.
    /// </summary>
    public async Task RecordUserInteractionAsync(string phone, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPhone = NormalizePhoneForCache(phone);
            var cacheKey = $"{ConversationKeyPrefix}{normalizedPhone}";

            await _cache.SetStringAsync(
                cacheKey,
                DateTime.UtcNow.ToString("O"),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ConversationWindowDuration
                },
                cancellationToken);

            _logger.LogInformation("WhatsApp conversation window opened/extended for {Phone}", normalizedPhone);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record WhatsApp interaction for {Phone}", phone);
        }
    }

    /// <summary>
    /// Gets the remaining time in the conversation window.
    /// </summary>
    public async Task<TimeSpan?> GetConversationTimeRemainingAsync(string phone, CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPhone = NormalizePhoneForCache(phone);
            var cacheKey = $"{ConversationKeyPrefix}{normalizedPhone}";
            var lastInteraction = await _cache.GetStringAsync(cacheKey, cancellationToken);

            if (!string.IsNullOrEmpty(lastInteraction) && DateTime.TryParse(lastInteraction, out var interactionTime))
            {
                var elapsed = DateTime.UtcNow - interactionTime;
                if (elapsed < ConversationWindowDuration)
                {
                    return ConversationWindowDuration - elapsed;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get WhatsApp conversation time remaining for {Phone}", phone);
            return null;
        }
    }

    private static string NormalizePhoneForCache(string phone)
    {
        // Extract just the digits for consistent cache keys
        return new string(phone.Where(char.IsDigit).ToArray());
    }

    /// <inheritdoc />
    public string FormatWhatsAppNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // Indian phone numbers
        if (digits.Length == 10)
        {
            return $"whatsapp:+91{digits}";
        }

        if (digits.Length == 12 && digits.StartsWith("91"))
        {
            return $"whatsapp:+{digits}";
        }

        if (phone.StartsWith("whatsapp:"))
        {
            return phone;
        }

        if (phone.StartsWith("+"))
        {
            return $"whatsapp:{phone}";
        }

        return $"whatsapp:+{digits}";
    }

    /// <inheritdoc />
    public async Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeTwilio();
            var account = await Twilio.Rest.Api.V2010.AccountResource.FetchAsync(_options.AccountSid);
            return account != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio WhatsApp configuration verification failed");
            return false;
        }
    }
}

#endregion

#region Push Channel Service (Firebase)

/// <summary>
/// Firebase FCM push notification service implementation
/// </summary>
public class FirebasePushService : IPushChannelService
{
    private readonly FirebaseOptions _options;
    private readonly ILogger<FirebasePushService> _logger;
    private bool _initialized;

    public FirebasePushService(
        IOptions<FirebaseOptions> options,
        ILogger<FirebasePushService> logger)
    {
        _options = options.Value;
        _logger = logger;
        InitializeFirebase();
    }

    private void InitializeFirebase()
    {
        if (_initialized || string.IsNullOrEmpty(_options.CredentialsJson))
            return;

        try
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                var credential = GoogleCredential.FromJson(_options.CredentialsJson);
                FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = _options.ProjectId });
            }
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Firebase");
        }
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendToTokenAsync(PushNotificationMessage message, string token, CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeFirebase();

            var fcmMessage = BuildMessage(message, token);
            var response = await FirebaseMessaging.DefaultInstance.SendAsync(fcmMessage, cancellationToken);

            _logger.LogInformation("Push sent to token, MessageId: {MessageId}", response);
            return new ChannelSendResult(true, response, null, null);
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            _logger.LogWarning("Push token is unregistered: {Token}", token[..20]);
            return new ChannelSendResult(false, null, "UNREGISTERED", "Token is no longer valid");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification");
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<List<ChannelSendResult>> SendToTokensAsync(PushNotificationMessage message, List<string> tokens, CancellationToken cancellationToken = default)
    {
        if (!tokens.Any())
            return [];

        try
        {
            InitializeFirebase();

            var multicastMessage = new MulticastMessage
            {
                Tokens = tokens,
                Notification = new FcmNotification
                {
                    Title = message.Title,
                    Body = message.Body,
                    ImageUrl = message.ImageUrl
                },
                Data = message.Data,
                Android = new AndroidConfig
                {
                    Priority = message.Priority == "high" ? Priority.High : Priority.Normal,
                    Notification = new AndroidNotification
                    {
                        ChannelId = message.ChannelId ?? "default",
                        Icon = "ic_notification",
                        Color = "#1e40af",
                        Sound = message.Sound ?? "default"
                    }
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps
                    {
                        Alert = new ApsAlert { Title = message.Title, Body = message.Body },
                        Sound = message.Sound ?? "default",
                        Badge = message.BadgeCount
                    }
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(multicastMessage, cancellationToken);

            var results = new List<ChannelSendResult>();
            for (int i = 0; i < response.Responses.Count; i++)
            {
                var r = response.Responses[i];
                if (r.IsSuccess)
                {
                    results.Add(new ChannelSendResult(true, r.MessageId, null, null));
                }
                else
                {
                    var errorCode = r.Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered
                        ? "UNREGISTERED"
                        : r.Exception?.MessagingErrorCode.ToString() ?? "UNKNOWN";
                    results.Add(new ChannelSendResult(false, null, errorCode, r.Exception?.Message));
                }
            }

            _logger.LogInformation("Multicast push sent: {Success}/{Total} succeeded",
                response.SuccessCount, tokens.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send multicast push notification");
            return tokens.Select(_ => new ChannelSendResult(false, null, "EXCEPTION", ex.Message)).ToList();
        }
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendToTopicAsync(PushNotificationMessage message, string topic, CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeFirebase();

            var fcmMessage = new Message
            {
                Topic = topic,
                Notification = new FcmNotification
                {
                    Title = message.Title,
                    Body = message.Body,
                    ImageUrl = message.ImageUrl
                },
                Data = message.Data
            };

            var response = await FirebaseMessaging.DefaultInstance.SendAsync(fcmMessage, cancellationToken);

            _logger.LogInformation("Push sent to topic {Topic}, MessageId: {MessageId}", topic, response);
            return new ChannelSendResult(true, response, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push to topic {Topic}", topic);
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> SubscribeToTopicAsync(string token, string topic, CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeFirebase();
            var response = await FirebaseMessaging.DefaultInstance.SubscribeToTopicAsync([token], topic);
            return response.SuccessCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe token to topic {Topic}", topic);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnsubscribeFromTopicAsync(string token, string topic, CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeFirebase();
            var response = await FirebaseMessaging.DefaultInstance.UnsubscribeFromTopicAsync([token], topic);
            return response.SuccessCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unsubscribe token from topic {Topic}", topic);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            InitializeFirebase();
            return Task.FromResult(FirebaseApp.DefaultInstance != null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase configuration verification failed");
            return Task.FromResult(false);
        }
    }

    private static Message BuildMessage(PushNotificationMessage message, string token)
    {
        return new Message
        {
            Token = token,
            Notification = new FcmNotification
            {
                Title = message.Title,
                Body = message.Body,
                ImageUrl = message.ImageUrl
            },
            Data = message.Data,
            Android = new AndroidConfig
            {
                Priority = message.Priority == "high" ? Priority.High : Priority.Normal,
                Notification = new AndroidNotification
                {
                    ChannelId = message.ChannelId ?? "default",
                    Icon = "ic_notification",
                    Color = "#1e40af",
                    Sound = message.Sound ?? "default"
                }
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Alert = new ApsAlert { Title = message.Title, Body = message.Body },
                    Sound = message.Sound ?? "default",
                    Badge = message.BadgeCount
                }
            }
        };
    }
}

#endregion

#region In-App Channel Service (WebSocket)

/// <summary>
/// In-app notification service using SignalR WebSockets
/// </summary>
public class InAppNotificationService : IInAppChannelService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<InAppNotificationService> _logger;

    public InAppNotificationService(
        IHubContext<NotificationHub> hubContext,
        IConnectionManager connectionManager,
        ILogger<InAppNotificationService> logger)
    {
        _hubContext = hubContext;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendAsync(InAppNotificationMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var connections = await _connectionManager.GetConnectionsAsync(message.UserId);

            if (!connections.Any())
            {
                _logger.LogDebug("User {UserId} has no active connections, notification stored only", message.UserId);
                return new ChannelSendResult(true, null, null, null);
            }

            var payload = new
            {
                type = "notification",
                payload = new
                {
                    id = message.NotificationId,
                    type = message.Type,
                    title = message.Title,
                    body = message.Body,
                    data = message.Data,
                    actionUrl = message.ActionUrl,
                    createdAt = message.CreatedAt ?? DateTime.UtcNow,
                    read = false
                }
            };

            await _hubContext.Clients.Clients(connections).SendAsync("notification", payload, cancellationToken);

            _logger.LogDebug("In-app notification sent to {ConnectionCount} connections for user {UserId}",
                connections.Count, message.UserId);

            return new ChannelSendResult(true, message.NotificationId.ToString(), null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send in-app notification to user {UserId}", message.UserId);
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<int> BroadcastToOrganizationAsync(Guid organizationId, InAppNotificationMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            var connections = await _connectionManager.GetOrganizationConnectionsAsync(organizationId);

            if (!connections.Any())
                return 0;

            var payload = new
            {
                type = "notification",
                payload = new
                {
                    id = message.NotificationId,
                    type = message.Type,
                    title = message.Title,
                    body = message.Body,
                    data = message.Data,
                    actionUrl = message.ActionUrl,
                    createdAt = message.CreatedAt ?? DateTime.UtcNow,
                    read = false
                }
            };

            await _hubContext.Clients.Clients(connections).SendAsync("notification", payload, cancellationToken);

            return connections.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast to organization {OrganizationId}", organizationId);
            return 0;
        }
    }

    /// <inheritdoc />
    public Task<bool> IsUserOnlineAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return _connectionManager.IsUserConnectedAsync(userId);
    }

    /// <inheritdoc />
    public Task<int> GetOnlineUserCountAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return _connectionManager.GetOnlineUserCountAsync(organizationId);
    }

    /// <inheritdoc />
    public async Task SendBadgeUpdateAsync(Guid userId, int unreadCount, CancellationToken cancellationToken = default)
    {
        try
        {
            var connections = await _connectionManager.GetConnectionsAsync(userId);

            if (!connections.Any())
                return;

            await _hubContext.Clients.Clients(connections).SendAsync("badgeUpdate", new { unreadCount }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send badge update to user {UserId}", userId);
        }
    }
}

#endregion

#region Push Token Service

/// <summary>
/// Push token management service implementation
/// </summary>
public class PushTokenService : IPushTokenService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PushTokenService> _logger;

    public PushTokenService(
        ApplicationDbContext dbContext,
        ILogger<PushTokenService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PushTokenDto> RegisterTokenAsync(Guid userId, RegisterPushTokenRequest request, CancellationToken cancellationToken = default)
    {
        // Check if token already exists
        var existing = await _dbContext.PushTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing != null)
        {
            // Update existing token (might be from a different user or re-registration)
            existing.UserId = userId;
            existing.Platform = request.Platform;
            existing.DeviceInfo = request.DeviceInfo ?? new Dictionary<string, object>();
            existing.IsActive = true;
            existing.LastUsedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated push token for user {UserId}, platform {Platform}", userId, request.Platform);

            return new PushTokenDto(existing.Id, existing.Platform, existing.CreatedAt, existing.LastUsedAt, existing.IsActive);
        }

        // Create new token
        var token = new PushToken
        {
            UserId = userId,
            Token = request.Token,
            Platform = request.Platform,
            DeviceInfo = request.DeviceInfo ?? new Dictionary<string, object>(),
            IsActive = true,
            LastUsedAt = DateTime.UtcNow
        };

        _dbContext.PushTokens.Add(token);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered new push token for user {UserId}, platform {Platform}", userId, request.Platform);

        return new PushTokenDto(token.Id, token.Platform, token.CreatedAt, token.LastUsedAt, token.IsActive);
    }

    /// <inheritdoc />
    public async Task<List<PushToken>> GetActiveTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.PushTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.IsActive)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeactivateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var pushToken = await _dbContext.PushTokens
            .FirstOrDefaultAsync(t => t.Token == token, cancellationToken);

        if (pushToken != null)
        {
            pushToken.IsActive = false;
            pushToken.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deactivated push token for user {UserId}", pushToken.UserId);
        }
    }

    /// <inheritdoc />
    public async Task MarkTokenInvalidAsync(string token, CancellationToken cancellationToken = default)
    {
        var pushToken = await _dbContext.PushTokens
            .FirstOrDefaultAsync(t => t.Token == token, cancellationToken);

        if (pushToken != null)
        {
            pushToken.IsActive = false;
            pushToken.UpdatedAt = DateTime.UtcNow;
            pushToken.DeviceInfo["invalidReason"] = "UNREGISTERED";
            pushToken.DeviceInfo["invalidatedAt"] = DateTime.UtcNow.ToString("O");
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Marked push token as invalid for user {UserId}", pushToken.UserId);
        }
    }

    /// <inheritdoc />
    public async Task UpdateLastUsedAsync(string token, CancellationToken cancellationToken = default)
    {
        await _dbContext.PushTokens
            .Where(t => t.Token == token)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, DateTime.UtcNow), cancellationToken);
    }

    /// <inheritdoc />
    public async Task CleanupInactiveTokensAsync(int daysOld = 90, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOld);

        var count = await _dbContext.PushTokens
            .Where(t => !t.IsActive && t.UpdatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Cleaned up {Count} inactive push tokens older than {Days} days", count, daysOld);
    }
}

#endregion

#region SignalR Hub and Connection Manager

/// <summary>
/// SignalR hub for real-time notifications
/// </summary>
public class NotificationHub : Hub
{
    private readonly IConnectionManager _connectionManager;
    private readonly INotificationEngineService _notificationService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        IConnectionManager connectionManager,
        INotificationEngineService notificationService,
        ILogger<NotificationHub> logger)
    {
        _connectionManager = connectionManager;
        _notificationService = notificationService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var organizationId = GetOrganizationId();

        if (userId.HasValue)
        {
            await _connectionManager.AddConnectionAsync(userId.Value, Context.ConnectionId, organizationId);
            _logger.LogDebug("User {UserId} connected, ConnectionId: {ConnectionId}", userId, Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();

        if (userId.HasValue)
        {
            await _connectionManager.RemoveConnectionAsync(userId.Value, Context.ConnectionId);
            _logger.LogDebug("User {UserId} disconnected, ConnectionId: {ConnectionId}", userId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Mark notification as read from client
    /// </summary>
    public async Task MarkAsRead(string notificationId)
    {
        var userId = GetUserId();
        if (!userId.HasValue || !Guid.TryParse(notificationId, out var nId))
            return;

        try
        {
            var result = await _notificationService.MarkAsReadAsync(nId, userId.Value);
            if (result.Read)
            {
                await Clients.User(userId.Value.ToString()).SendAsync("notificationRead", notificationId);
                _logger.LogDebug("Notification {NotificationId} marked as read for user {UserId}", nId, userId.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark notification {NotificationId} as read for user {UserId}", nId, userId.Value);
        }
    }

    private Guid? GetUserId()
    {
        var claim = Context.User?.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }

    private Guid? GetOrganizationId()
    {
        var claim = Context.User?.FindFirst("organization_id");
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }
}

/// <summary>
/// Interface for managing SignalR connections
/// </summary>
public interface IConnectionManager
{
    Task AddConnectionAsync(Guid userId, string connectionId, Guid? organizationId = null);
    Task RemoveConnectionAsync(Guid userId, string connectionId);
    Task<List<string>> GetConnectionsAsync(Guid userId);
    Task<List<string>> GetOrganizationConnectionsAsync(Guid organizationId);
    Task<bool> IsUserConnectedAsync(Guid userId);
    Task<int> GetOnlineUserCountAsync(Guid organizationId);
}

/// <summary>
/// In-memory connection manager (use Redis for production with multiple servers)
/// </summary>
public class InMemoryConnectionManager : IConnectionManager
{
    private readonly Dictionary<Guid, HashSet<string>> _userConnections = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _organizationUsers = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public Task AddConnectionAsync(Guid userId, string connectionId, Guid? organizationId = null)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_userConnections.ContainsKey(userId))
                _userConnections[userId] = new HashSet<string>();

            _userConnections[userId].Add(connectionId);

            if (organizationId.HasValue)
            {
                if (!_organizationUsers.ContainsKey(organizationId.Value))
                    _organizationUsers[organizationId.Value] = new HashSet<Guid>();

                _organizationUsers[organizationId.Value].Add(userId);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(Guid userId, string connectionId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                    _userConnections.Remove(userId);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    public Task<List<string>> GetConnectionsAsync(Guid userId)
    {
        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(_userConnections.TryGetValue(userId, out var connections)
                ? connections.ToList()
                : new List<string>());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Task<List<string>> GetOrganizationConnectionsAsync(Guid organizationId)
    {
        _lock.EnterReadLock();
        try
        {
            var connections = new List<string>();

            if (_organizationUsers.TryGetValue(organizationId, out var users))
            {
                foreach (var userId in users)
                {
                    if (_userConnections.TryGetValue(userId, out var userConns))
                        connections.AddRange(userConns);
                }
            }

            return Task.FromResult(connections);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Task<bool> IsUserConnectedAsync(Guid userId)
    {
        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(_userConnections.ContainsKey(userId) && _userConnections[userId].Count > 0);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Task<int> GetOnlineUserCountAsync(Guid organizationId)
    {
        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(_organizationUsers.TryGetValue(organizationId, out var users)
                ? users.Count(u => _userConnections.ContainsKey(u) && _userConnections[u].Count > 0)
                : 0);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}

#endregion
