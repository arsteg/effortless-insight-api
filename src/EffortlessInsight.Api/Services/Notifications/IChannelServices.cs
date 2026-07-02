namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Base result for all channel send operations
/// </summary>
public record ChannelSendResult(
    bool Success,
    string? MessageId,
    string? ErrorCode,
    string? ErrorMessage
);

#region Email Channel

/// <summary>
/// Email sending service (Resend)
/// </summary>
public interface IEmailChannelService
{
    /// <summary>
    /// Send an email notification
    /// </summary>
    Task<ChannelSendResult> SendAsync(EmailNotificationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify email configuration
    /// </summary>
    Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Email notification message
/// </summary>
public record EmailNotificationMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    string? NotificationId = null,
    string? UserId = null,
    Dictionary<string, string>? CustomArgs = null,
    string? ReplyTo = null,
    List<EmailAttachment>? Attachments = null
);

/// <summary>
/// Email attachment
/// </summary>
public record EmailAttachment(
    string FileName,
    string ContentType,
    byte[] Content
);

#endregion

#region SMS Channel

/// <summary>
/// SMS sending service (Twilio)
/// </summary>
public interface ISmsChannelService
{
    /// <summary>
    /// Send an SMS notification
    /// </summary>
    Task<ChannelSendResult> SendAsync(SmsNotificationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an OTP code
    /// </summary>
    Task<ChannelSendResult> SendOtpAsync(string phone, string code, int expiryMinutes = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify Twilio configuration
    /// </summary>
    Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Format phone number to E.164 format (Indian numbers)
    /// </summary>
    string FormatPhoneNumber(string phone);
}

/// <summary>
/// SMS notification message
/// </summary>
public record SmsNotificationMessage(
    string ToPhone,
    string Body,
    string? NotificationId = null,
    bool IsUrgent = false
);

#endregion

#region Push Channel

/// <summary>
/// Push notification service (Firebase FCM)
/// </summary>
public interface IPushChannelService
{
    /// <summary>
    /// Send push notification to a specific token
    /// </summary>
    Task<ChannelSendResult> SendToTokenAsync(PushNotificationMessage message, string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send push notification to multiple tokens (same user, multiple devices)
    /// </summary>
    Task<List<ChannelSendResult>> SendToTokensAsync(PushNotificationMessage message, List<string> tokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send push notification to a topic
    /// </summary>
    Task<ChannelSendResult> SendToTopicAsync(PushNotificationMessage message, string topic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe token to a topic
    /// </summary>
    Task<bool> SubscribeToTopicAsync(string token, string topic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribe token from a topic
    /// </summary>
    Task<bool> UnsubscribeFromTopicAsync(string token, string topic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify Firebase configuration
    /// </summary>
    Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Push notification message
/// </summary>
public record PushNotificationMessage(
    string Title,
    string Body,
    Dictionary<string, string> Data,
    string? ImageUrl = null,
    string? NotificationId = null,
    string Priority = "normal",  // normal, high
    string? ChannelId = null,    // Android notification channel
    string? Sound = null,
    int? BadgeCount = null,
    string? DeepLink = null,
    string? ActionUrl = null
);

#endregion

#region WhatsApp Channel

/// <summary>
/// WhatsApp notification service (Twilio WhatsApp Business API)
/// </summary>
public interface IWhatsAppChannelService
{
    /// <summary>
    /// Send a WhatsApp template message
    /// </summary>
    Task<ChannelSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a WhatsApp text message (only for active conversations)
    /// </summary>
    Task<ChannelSendResult> SendTextAsync(string toPhone, string text, string? notificationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a conversation is active (user has responded within 24 hours)
    /// </summary>
    Task<bool> IsConversationActiveAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Format phone number for WhatsApp (with whatsapp: prefix)
    /// </summary>
    string FormatWhatsAppNumber(string phone);

    /// <summary>
    /// Verify Twilio WhatsApp configuration
    /// </summary>
    Task<bool> VerifyConfigurationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// WhatsApp template message
/// </summary>
public record WhatsAppTemplateMessage(
    string ToPhone,
    string TemplateSid,  // Twilio content template SID
    Dictionary<string, string> Variables,
    string? NotificationId = null,
    string Language = "en"
);

#endregion

#region In-App Channel

/// <summary>
/// In-app real-time notification service (WebSocket)
/// </summary>
public interface IInAppChannelService
{
    /// <summary>
    /// Send real-time notification to connected clients
    /// </summary>
    Task<ChannelSendResult> SendAsync(InAppNotificationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send to all users in an organization
    /// </summary>
    Task<int> BroadcastToOrganizationAsync(Guid organizationId, InAppNotificationMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user is currently connected
    /// </summary>
    Task<bool> IsUserOnlineAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get online user count for an organization
    /// </summary>
    Task<int> GetOnlineUserCountAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send badge count update
    /// </summary>
    Task SendBadgeUpdateAsync(Guid userId, int unreadCount, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-app notification message
/// </summary>
public record InAppNotificationMessage(
    Guid UserId,
    Guid NotificationId,
    string Type,
    string Title,
    string Body,
    Dictionary<string, object> Data,
    string? ActionUrl = null,
    DateTime? CreatedAt = null
);

#endregion

#region Channel Configuration

/// <summary>
/// Resend configuration options
/// </summary>
public class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "notifications@effortlessinsight.com";
    public string FromName { get; set; } = "EffortlessInsight";
    public string? ReplyTo { get; set; }
}


/// <summary>
/// Firebase configuration options
/// </summary>
public class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string ProjectId { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// Web Push VAPID keys for browser push notifications
    /// </summary>
    public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKey { get; set; }

    /// <summary>
    /// Android notification channel configurations
    /// </summary>
    public Dictionary<string, AndroidChannelConfig> AndroidChannels { get; set; } = new();

    /// <summary>
    /// Generate credentials JSON from individual fields for Firebase Admin SDK
    /// </summary>
    public string GetCredentialsJson()
    {
        if (string.IsNullOrEmpty(ProjectId) || string.IsNullOrEmpty(PrivateKey) || string.IsNullOrEmpty(ClientEmail))
            return string.Empty;

        // Handle escaped newlines in private key (common when passed via environment variables)
        var normalizedPrivateKey = PrivateKey.Replace("\\n", "\n");

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = ProjectId,
            private_key_id = "firebase-admin-key",
            private_key = normalizedPrivateKey,
            client_email = ClientEmail,
            client_id = "",
            auth_uri = "https://accounts.google.com/o/oauth2/auth",
            token_uri = "https://oauth2.googleapis.com/token",
            auth_provider_x509_cert_url = "https://www.googleapis.com/oauth2/v1/certs",
            client_x509_cert_url = $"https://www.googleapis.com/robot/v1/metadata/x509/{Uri.EscapeDataString(ClientEmail)}"
        });
    }

    /// <summary>
    /// Check if Firebase is properly configured
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(ProjectId) &&
        !string.IsNullOrEmpty(PrivateKey) &&
        !string.IsNullOrEmpty(ClientEmail);
}

/// <summary>
/// Android notification channel configuration
/// </summary>
public class AndroidChannelConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Importance { get; set; } = "default";  // low, default, high
    public string? Sound { get; set; }
    public bool Vibration { get; set; } = true;
}

#endregion
