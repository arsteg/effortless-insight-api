using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

#region SMS Channel Service (Disabled)

/// <summary>
/// No-op SMS channel service implementation.
/// SMS functionality is disabled. To enable, integrate with a provider (e.g., Twilio, AWS SNS).
/// </summary>
public class DisabledSmsService : ISmsChannelService
{
    private readonly ILogger<DisabledSmsService> _logger;

    public DisabledSmsService(ILogger<DisabledSmsService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ChannelSendResult> SendAsync(SmsNotificationMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("SMS channel is disabled. Message to {Phone} was not sent.", message.ToPhone);
        return Task.FromResult(new ChannelSendResult(false, null, "DISABLED", "SMS channel is not configured"));
    }

    /// <inheritdoc />
    public Task<ChannelSendResult> SendOtpAsync(string phone, string code, int expiryMinutes = 10, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("SMS channel is disabled. OTP to {Phone} was not sent.", phone);
        return Task.FromResult(new ChannelSendResult(false, null, "DISABLED", "SMS channel is not configured"));
    }

    /// <inheritdoc />
    public Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
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

#region WhatsApp Channel Service (Meta WhatsApp Business API)

/// <summary>
/// Meta WhatsApp Business API channel service implementation.
/// Uses the Graph API for sending WhatsApp messages.
/// </summary>
public class MetaWhatsAppChannelService : IWhatsAppChannelService
{
    private readonly WhatsApp.IMetaWhatsAppClient _client;
    private readonly Options.MetaWhatsAppOptions _options;
    private readonly IDistributedCache _cache;
    private readonly ILogger<MetaWhatsAppChannelService> _logger;

    // WhatsApp conversation window is 24 hours
    private const string ConversationKeyPrefix = "whatsapp:conversation:";

    public MetaWhatsAppChannelService(
        WhatsApp.IMetaWhatsAppClient client,
        IOptions<Options.MetaWhatsAppOptions> options,
        IDistributedCache cache,
        ILogger<MetaWhatsAppChannelService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("WhatsApp channel is disabled. Template message to {Phone} was not sent.", message.ToPhone);
            return new ChannelSendResult(false, null, "DISABLED", "WhatsApp channel is not enabled");
        }

        try
        {
            var formattedPhone = _client.FormatPhoneNumber(message.ToPhone);

            // Convert variables dictionary to template parameters
            var bodyParameters = message.Variables
                .Select(v => new DTOs.TemplateParameter("text", v.Value))
                .ToList();

            var result = await _client.SendTemplateMessageAsync(
                formattedPhone,
                message.TemplateSid, // TemplateSid is used as template name
                message.Language,
                bodyParameters,
                null,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("WhatsApp template sent successfully to {Phone}, MessageId: {MessageId}",
                    _client.MaskPhoneNumber(formattedPhone), result.MessageId);
                return new ChannelSendResult(true, result.MessageId, null, null);
            }

            _logger.LogError("WhatsApp template send failed: {ErrorCode} - {ErrorMessage}",
                result.ErrorCode, result.ErrorMessage);
            return new ChannelSendResult(false, null, result.ErrorCode, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp template to {Phone}", message.ToPhone);
            return new ChannelSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<ChannelSendResult> SendTextAsync(string toPhone, string text, string? notificationId = null, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("WhatsApp channel is disabled. Text message to {Phone} was not sent.", toPhone);
            return new ChannelSendResult(false, null, "DISABLED", "WhatsApp channel is not enabled");
        }

        try
        {
            var formattedPhone = _client.FormatPhoneNumber(toPhone);

            var result = await _client.SendTextMessageAsync(
                formattedPhone,
                text,
                false,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("WhatsApp text sent successfully to {Phone}, MessageId: {MessageId}",
                    _client.MaskPhoneNumber(formattedPhone), result.MessageId);
                return new ChannelSendResult(true, result.MessageId, null, null);
            }

            _logger.LogError("WhatsApp text send failed: {ErrorCode} - {ErrorMessage}",
                result.ErrorCode, result.ErrorMessage);
            return new ChannelSendResult(false, null, result.ErrorCode, result.ErrorMessage);
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

            // Check if within conversation window
            if (DateTime.TryParse(lastInteraction, out var interactionTime))
            {
                var windowDuration = TimeSpan.FromHours(_options.ConversationWindowHours);
                var isActive = DateTime.UtcNow - interactionTime < windowDuration;
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
            // Return false to fall back to template messages
            return false;
        }
    }

    /// <inheritdoc />
    public string FormatWhatsAppNumber(string phone)
    {
        return _client.FormatPhoneNumber(phone);
    }

    /// <inheritdoc />
    public Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default)
    {
        // Check if required configuration is present
        var isConfigured = _options.Enabled &&
                          !string.IsNullOrEmpty(_options.AccessToken) &&
                          !string.IsNullOrEmpty(_options.PhoneNumberId) &&
                          !string.IsNullOrEmpty(_options.WabaId);

        return Task.FromResult(isConfigured);
    }

    private static string NormalizePhoneForCache(string phone)
    {
        // Extract just the digits for consistent cache keys
        return new string(phone.Where(char.IsDigit).ToArray());
    }
}

#endregion

#region Push Channel Service (Firebase)

/// <summary>
/// Push notification service. Routes native FCM registration tokens through the
/// Firebase Admin SDK and Expo push tokens (ExponentPushToken[...]) through
/// Expo's push service, so the managed Expo mobile app and the FCM web client
/// can share a single push channel (audit CC-02).
/// </summary>
public class FirebasePushService : IPushChannelService
{
    private const string ExpoTokenPrefix = "ExponentPushToken[";
    private const string ExpoTokenPrefixAlt = "ExpoPushToken[";
    private const string ExpoPushEndpoint = "https://exp.host/--/api/v2/push/send";
    private const int ExpoChunkSize = 100; // Expo accepts up to 100 messages per request

    private static readonly JsonSerializerOptions ExpoJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly FirebaseOptions _options;
    private readonly ILogger<FirebasePushService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private bool _initialized;

    public FirebasePushService(
        IOptions<FirebaseOptions> options,
        ILogger<FirebasePushService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        InitializeFirebase();
    }

    private static bool IsExpoToken(string token) =>
        token.StartsWith(ExpoTokenPrefix, StringComparison.Ordinal)
        || token.StartsWith(ExpoTokenPrefixAlt, StringComparison.Ordinal);

    private void InitializeFirebase()
    {
        if (_initialized)
            return;

        // Check if Firebase is configured
        if (!_options.IsConfigured)
        {
            _logger.LogWarning("Firebase is not configured. Push notifications will not work.");
            return;
        }

        try
        {
            if (FirebaseApp.DefaultInstance == null)
            {
                GoogleCredential credential;

                // Try to load from credentials file path first
                if (!string.IsNullOrEmpty(_options.CredentialsPath) && System.IO.File.Exists(_options.CredentialsPath))
                {
                    credential = GoogleCredential.FromFile(_options.CredentialsPath);
                    _logger.LogInformation("Firebase initialized from credentials file: {Path}", _options.CredentialsPath);
                }
                else
                {
                    // Generate credentials JSON from individual fields
                    var credentialsJson = _options.GetCredentialsJson();
                    if (string.IsNullOrEmpty(credentialsJson))
                    {
                        _logger.LogWarning("Firebase credentials could not be generated. Check ProjectId, PrivateKey, and ClientEmail configuration.");
                        return;
                    }

                    credential = GoogleCredential.FromJson(credentialsJson);
                    _logger.LogInformation("Firebase initialized from individual credential fields for project: {ProjectId}", _options.ProjectId);
                }

                FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = _options.ProjectId });
            }
            _initialized = true;
            _logger.LogInformation("Firebase Admin SDK initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Firebase Admin SDK");
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

        // Partition tokens by type while remembering their original positions,
        // so the returned results line up with the input list. The engine's
        // invalid-token cleanup matches results to tokens by index.
        var results = new ChannelSendResult[tokens.Count];
        var fcmIndices = new List<int>();
        var expoIndices = new List<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (IsExpoToken(tokens[i]))
                expoIndices.Add(i);
            else
                fcmIndices.Add(i);
        }

        if (fcmIndices.Count > 0)
        {
            var fcmTokens = fcmIndices.Select(i => tokens[i]).ToList();
            var fcmResults = await SendToFcmTokensAsync(message, fcmTokens, cancellationToken);
            for (int j = 0; j < fcmIndices.Count && j < fcmResults.Count; j++)
                results[fcmIndices[j]] = fcmResults[j];
        }

        if (expoIndices.Count > 0)
        {
            var expoTokens = expoIndices.Select(i => tokens[i]).ToList();
            var expoResults = await SendToExpoTokensAsync(message, expoTokens, cancellationToken);
            for (int j = 0; j < expoIndices.Count && j < expoResults.Count; j++)
                results[expoIndices[j]] = expoResults[j];
        }

        // Guard against any position left unset (defensive; both helpers return
        // one result per input token).
        for (int i = 0; i < results.Length; i++)
            results[i] ??= new ChannelSendResult(false, null, "NO_RESULT", "No push result produced");

        return results.ToList();
    }

    private async Task<List<ChannelSendResult>> SendToFcmTokensAsync(PushNotificationMessage message, List<string> tokens, CancellationToken cancellationToken)
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
                Android = BuildAndroidConfig(message),
                Apns = BuildApnsConfig(message),
                Webpush = BuildWebpushConfig(message)
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

    /// <summary>
    /// Delivers to Expo push tokens via Expo's push service, chunked at 100 per
    /// request. Returns one result per input token, in order. Expo's
    /// "DeviceNotRegistered" is mapped to "UNREGISTERED" so the engine's
    /// existing invalid-token cleanup deactivates the token, exactly as it does
    /// for FCM. Note: this inspects the immediate push *tickets*; authoritative
    /// invalid-token detection also arrives later via Expo *receipts*, which a
    /// follow-up background job should poll (audit BE-25 / Phase 2).
    /// </summary>
    private async Task<List<ChannelSendResult>> SendToExpoTokensAsync(PushNotificationMessage message, List<string> tokens, CancellationToken cancellationToken)
    {
        var results = new List<ChannelSendResult>(tokens.Count);

        try
        {
            using var http = _httpClientFactory.CreateClient();
            if (!string.IsNullOrEmpty(_options.ExpoAccessToken))
            {
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ExpoAccessToken);
            }

            for (int start = 0; start < tokens.Count; start += ExpoChunkSize)
            {
                var chunk = tokens.Skip(start).Take(ExpoChunkSize).ToList();
                var payload = new ExpoPushRequest
                {
                    To = chunk,
                    Title = message.Title,
                    Body = message.Body,
                    Data = message.Data,
                    Sound = message.Sound ?? "default",
                    Priority = message.Priority == "high" ? "high" : "default",
                    ChannelId = message.ChannelId,
                    Badge = message.BadgeCount,
                    Ttl = message.TimeToLive.HasValue ? (int)message.TimeToLive.Value.TotalSeconds : null
                };

                using var response = await http.PostAsJsonAsync(
                    ExpoPushEndpoint, payload, ExpoJsonOptions, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Expo push request failed: {Status} {Body}", (int)response.StatusCode, body);
                    results.AddRange(chunk.Select(_ =>
                        new ChannelSendResult(false, null, "EXPO_HTTP_ERROR", $"HTTP {(int)response.StatusCode}")));
                    continue;
                }

                var parsed = await response.Content.ReadFromJsonAsync<ExpoPushResponse>(ExpoJsonOptions, cancellationToken);
                var tickets = parsed?.Data;

                if (tickets == null || tickets.Count != chunk.Count)
                {
                    _logger.LogWarning(
                        "Expo push response shape unexpected (expected {Expected} tickets, got {Got})",
                        chunk.Count, tickets?.Count ?? 0);
                    results.AddRange(chunk.Select(_ =>
                        new ChannelSendResult(false, null, "EXPO_BAD_RESPONSE", "Unexpected Expo response")));
                    continue;
                }

                foreach (var ticket in tickets)
                {
                    if (string.Equals(ticket.Status, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new ChannelSendResult(true, ticket.Id, null, null));
                    }
                    else
                    {
                        var expoError = ticket.Details?.Error;
                        var errorCode = string.Equals(expoError, "DeviceNotRegistered", StringComparison.OrdinalIgnoreCase)
                            ? "UNREGISTERED"
                            : expoError ?? "EXPO_ERROR";
                        results.Add(new ChannelSendResult(false, null, errorCode, ticket.Message));
                    }
                }
            }

            _logger.LogInformation("Expo push sent: {Success}/{Total} succeeded",
                results.Count(r => r.Success), tokens.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Expo push notifications");
            while (results.Count < tokens.Count)
                results.Add(new ChannelSendResult(false, null, "EXCEPTION", ex.Message));
            return results;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ExpoReceiptResult>> CheckExpoReceiptsAsync(
        IEnumerable<string> ticketIds, CancellationToken cancellationToken = default)
    {
        var ids = ticketIds.Distinct().ToList();
        var results = new Dictionary<string, ExpoReceiptResult>();
        if (ids.Count == 0)
            return results;

        try
        {
            using var http = _httpClientFactory.CreateClient();
            if (!string.IsNullOrEmpty(_options.ExpoAccessToken))
            {
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ExpoAccessToken);
            }

            // Expo accepts up to 1000 receipt ids per request.
            for (int start = 0; start < ids.Count; start += 1000)
            {
                var chunk = ids.Skip(start).Take(1000).ToList();
                using var response = await http.PostAsJsonAsync(
                    "https://exp.host/--/api/v2/push/getReceipts",
                    new { ids = chunk }, ExpoJsonOptions, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Expo getReceipts failed: {Status}", (int)response.StatusCode);
                    continue;
                }

                var parsed = await response.Content.ReadFromJsonAsync<ExpoReceiptsResponse>(ExpoJsonOptions, cancellationToken);
                if (parsed?.Data == null)
                    continue;

                foreach (var (ticketId, receipt) in parsed.Data)
                {
                    var ok = string.Equals(receipt.Status, "ok", StringComparison.OrdinalIgnoreCase);
                    results[ticketId] = new ExpoReceiptResult(ok, receipt.Details?.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Expo push receipts");
        }

        return results;
    }

    private sealed record ExpoReceiptsResponse
    {
        public Dictionary<string, ExpoReceipt>? Data { get; init; }
    }

    private sealed record ExpoReceipt
    {
        public string? Status { get; init; }
        public string? Message { get; init; }
        public ExpoPushTicketDetails? Details { get; init; }
    }

    private sealed record ExpoPushRequest
    {
        public required List<string> To { get; init; }
        public string? Title { get; init; }
        public string? Body { get; init; }
        public Dictionary<string, string>? Data { get; init; }
        public string? Sound { get; init; }
        public string? Priority { get; init; }
        public string? ChannelId { get; init; }
        public int? Badge { get; init; }
        public int? Ttl { get; init; }
    }

    private sealed record ExpoPushResponse
    {
        public List<ExpoPushTicket>? Data { get; init; }
    }

    private sealed record ExpoPushTicket
    {
        public string? Status { get; init; }
        public string? Id { get; init; }
        public string? Message { get; init; }
        public ExpoPushTicketDetails? Details { get; init; }
    }

    private sealed record ExpoPushTicketDetails
    {
        public string? Error { get; init; }
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
            Android = BuildAndroidConfig(message),
            Apns = BuildApnsConfig(message),
            Webpush = BuildWebpushConfig(message)
        };
    }

    // Shared platform-config builders so single-token and multicast sends stay
    // consistent and carry click action, TTL, collapse key, badge and web link
    // (audit BE-16 / BE-17 / BE-18 / BE-19).
    private static AndroidConfig BuildAndroidConfig(PushNotificationMessage message) => new()
    {
        Priority = message.Priority == "high" ? Priority.High : Priority.Normal,
        CollapseKey = message.CollapseKey,
        TimeToLive = message.TimeToLive,
        Notification = new AndroidNotification
        {
            ChannelId = message.ChannelId ?? "default",
            Icon = "ic_notification",
            Color = "#1e40af",
            Sound = message.Sound ?? "default",
            ClickAction = "OPEN_NOTIFICATION"
        }
    };

    private static ApnsConfig BuildApnsConfig(PushNotificationMessage message)
    {
        var config = new ApnsConfig
        {
            Aps = new Aps
            {
                Alert = new ApsAlert { Title = message.Title, Body = message.Body },
                Sound = message.Sound ?? "default",
                Badge = message.BadgeCount
            }
        };

        if (message.TimeToLive.HasValue)
        {
            config.Headers = new Dictionary<string, string>
            {
                ["apns-expiration"] = DateTimeOffset.UtcNow.Add(message.TimeToLive.Value).ToUnixTimeSeconds().ToString()
            };
        }

        return config;
    }

    private static WebpushConfig? BuildWebpushConfig(PushNotificationMessage message)
    {
        var link = message.ActionUrl != null
            ? $"https://app.effortlessinsight.com{message.ActionUrl}"
            : null;

        return new WebpushConfig
        {
            FcmOptions = link != null ? new WebpushFcmOptions { Link = link } : null,
            Notification = new WebpushNotification
            {
                Title = message.Title,
                Body = message.Body,
                Icon = "/small-logo.png"
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

            // Target the user through SignalR's user-routing so delivery works
            // across the Redis backplane regardless of which node holds the
            // connection (or if this runs in a Hangfire worker). The previous
            // code targeted only this node's in-memory connection list and
            // silently dropped everything else (audit BE-11).
            await _hubContext.Clients.User(message.UserId.ToString())
                .SendAsync("notification", payload, cancellationToken);

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

            // Broadcast to the org group (connections join it in the hub's
            // OnConnectedAsync). Backplane-aware, unlike the per-node connection
            // list (audit BE-11). The exact recipient count is not known across
            // the backplane; a best-effort local count is returned for logging.
            await _hubContext.Clients.Group($"org:{organizationId}")
                .SendAsync("notification", payload, cancellationToken);

            return await _connectionManager.GetOnlineUserCountAsync(organizationId);
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
            await _hubContext.Clients.User(userId.ToString())
                .SendAsync("badgeUpdate", new { unreadCount }, cancellationToken);
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

    private const int MaxTokensPerUser = 20;
    private static readonly string[] ValidPlatforms = { "ios", "android", "web" };

    /// <inheritdoc />
    public async Task<PushTokenDto> RegisterTokenAsync(Guid userId, RegisterPushTokenRequest request, CancellationToken cancellationToken = default)
    {
        // Validate input (audit BE-13): bounded token, known platform.
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length is < 10 or > 4096)
            throw new ArgumentException("Invalid push token", nameof(request));

        var platform = (request.Platform ?? string.Empty).ToLowerInvariant();
        if (!ValidPlatforms.Contains(platform))
            throw new ArgumentException($"Invalid platform '{request.Platform}'", nameof(request));

        // Check if token already exists
        var existing = await _dbContext.PushTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing != null)
        {
            // A device re-registering its own token is normal. Reassigning a
            // token across users can happen on device handoff but is also the
            // shape of a hijack attempt, so log it as a security-relevant event
            // (audit BE-13). Proper device attestation is a larger follow-up.
            if (existing.UserId != userId)
            {
                _logger.LogWarning(
                    "Push token reassigned from user {OldUser} to user {NewUser}",
                    existing.UserId, userId);
            }

            existing.UserId = userId;
            existing.Platform = platform;
            existing.DeviceInfo = request.DeviceInfo ?? new Dictionary<string, object>();
            existing.IsActive = true;
            existing.LastUsedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated push token for user {UserId}, platform {Platform}", userId, platform);

            return new PushTokenDto(existing.Id, existing.Platform, existing.CreatedAt, existing.LastUsedAt, existing.IsActive);
        }

        // Enforce a per-user cap: deactivate the oldest active tokens beyond the
        // limit so the token table cannot grow unbounded (audit BE-13/BE-21).
        var activeCount = await _dbContext.PushTokens
            .CountAsync(t => t.UserId == userId && t.IsActive, cancellationToken);
        if (activeCount >= MaxTokensPerUser)
        {
            var toRetire = await _dbContext.PushTokens
                .Where(t => t.UserId == userId && t.IsActive)
                .OrderBy(t => t.LastUsedAt)
                .Take(activeCount - MaxTokensPerUser + 1)
                .ToListAsync(cancellationToken);
            foreach (var old in toRetire)
            {
                old.IsActive = false;
                old.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Create new token
        var token = new PushToken
        {
            UserId = userId,
            Token = request.Token,
            Platform = platform,
            DeviceInfo = request.DeviceInfo ?? new Dictionary<string, object>(),
            IsActive = true,
            LastUsedAt = DateTime.UtcNow
        };

        _dbContext.PushTokens.Add(token);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Registered new push token for user {UserId}, platform {Platform}", userId, platform);

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
    public async Task<bool> DeactivateTokenAsync(string token, Guid userId, CancellationToken cancellationToken = default)
    {
        // Scope to the owner so a caller cannot deactivate someone else's token
        // by knowing its value (audit BE-13).
        var pushToken = await _dbContext.PushTokens
            .FirstOrDefaultAsync(t => t.Token == token && t.UserId == userId, cancellationToken);

        if (pushToken == null)
            return false;

        pushToken.IsActive = false;
        pushToken.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deactivated push token for user {UserId}", pushToken.UserId);
        return true;
    }

    /// <inheritdoc />
    public async Task TouchTokensAsync(IEnumerable<string> tokens, CancellationToken cancellationToken = default)
    {
        var list = tokens.Distinct().ToList();
        if (list.Count == 0)
            return;

        await _dbContext.PushTokens
            .Where(t => list.Contains(t.Token))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, DateTime.UtcNow), cancellationToken);
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

        // Also retire tokens that are still "active" but haven't been used in a
        // long time — these are almost always uninstalled apps that never sent
        // an UNREGISTERED signal, and every send keeps fanning out to them
        // (audit BE-21). FCM guidance is to prune tokens unused ~270 days; we use
        // a more conservative window.
        var staleActiveCutoff = DateTime.UtcNow.AddDays(-Math.Max(daysOld, 60));
        var staleActive = await _dbContext.PushTokens
            .Where(t => t.IsActive && t.LastUsedAt < staleActiveCutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false), cancellationToken);

        _logger.LogInformation(
            "Cleaned up {Count} inactive push tokens older than {Days} days; retired {Stale} stale-active tokens",
            count, daysOld, staleActive);
    }
}

#endregion

#region SignalR Hub and Connection Manager

/// <summary>
/// SignalR hub for real-time notifications
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize]
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

            // Join an org group so backplane-aware org broadcasts reach this
            // connection (audit BE-11). SignalR removes the connection from its
            // groups automatically on disconnect.
            if (organizationId.HasValue)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"org:{organizationId.Value}");

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
/// Maps a SignalR connection to a stable user id for Clients.User(...) routing.
/// The app authenticates with the JWT "sub" claim and disables inbound claim
/// remapping, so the default provider (which reads ClaimTypes.NameIdentifier)
/// would return null and Clients.User would target no one (audit BE-11).
/// </summary>
public class SignalRUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst("sub")?.Value
            ?? connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
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
