namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for Meta WhatsApp Cloud API integration.
/// </summary>
public class MetaWhatsAppOptions
{
    public const string SectionName = "MetaWhatsApp";

    // Meta App Configuration
    /// <summary>
    /// Meta App ID from developers.facebook.com.
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// Meta App Secret for webhook signature verification.
    /// </summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// System User permanent access token with whatsapp_business_messaging permission.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Webhook verification token (you define this, Meta verifies it).
    /// </summary>
    public string VerifyToken { get; set; } = string.Empty;

    // WhatsApp Business Account Configuration
    /// <summary>
    /// WhatsApp Business Account ID.
    /// </summary>
    public string WabaId { get; set; } = string.Empty;

    /// <summary>
    /// Business phone number ID (from WhatsApp Manager).
    /// </summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>
    /// Display phone number with country code (e.g., +919876543210).
    /// </summary>
    public string DisplayPhoneNumber { get; set; } = string.Empty;

    // API Configuration
    /// <summary>
    /// Meta Graph API version (e.g., v18.0).
    /// </summary>
    public string GraphApiVersion { get; set; } = "v18.0";

    /// <summary>
    /// Meta Graph API base URL.
    /// </summary>
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";

    // Bot Configuration
    /// <summary>
    /// Whether the WhatsApp bot is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Verification code expiry in minutes.
    /// </summary>
    public int VerificationCodeExpiryMinutes { get; set; } = 10;

    /// <summary>
    /// Maximum verification attempts before lockout.
    /// </summary>
    public int MaxVerificationAttempts { get; set; } = 3;

    /// <summary>
    /// Session timeout in minutes (for conversation state).
    /// </summary>
    public int SessionTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// WhatsApp conversation window in hours (typically 24).
    /// </summary>
    public int ConversationWindowHours { get; set; } = 24;

    // Rate Limiting
    /// <summary>
    /// Maximum messages per user per day.
    /// </summary>
    public int MaxMessagesPerUserPerDay { get; set; } = 50;

    /// <summary>
    /// Rate limit per minute per phone number.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 10;

    // Features
    /// <summary>
    /// Whether daily digest is enabled.
    /// </summary>
    public bool DailyDigestEnabled { get; set; } = true;

    /// <summary>
    /// Time to send daily digest in IST (HH:mm format).
    /// </summary>
    public string DailyDigestTimeIST { get; set; } = "09:00";

    /// <summary>
    /// Whether to send deadline reminders via WhatsApp.
    /// </summary>
    public bool DeadlineRemindersEnabled { get; set; } = true;

    /// <summary>
    /// Whether to send high-risk alerts via WhatsApp.
    /// </summary>
    public bool HighRiskAlertsEnabled { get; set; } = true;

    /// <summary>
    /// Whether to send task assignment notifications via WhatsApp.
    /// </summary>
    public bool TaskAssignmentsEnabled { get; set; } = true;

    // Retry Configuration
    /// <summary>
    /// Maximum retry attempts for failed messages.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Initial retry delay in seconds.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Computed base URL for API calls.
    /// </summary>
    public string ApiBaseUrl => $"{GraphApiBaseUrl}/{GraphApiVersion}";

    /// <summary>
    /// Messages endpoint URL.
    /// </summary>
    public string MessagesUrl => $"{ApiBaseUrl}/{PhoneNumberId}/messages";

    /// <summary>
    /// Media upload endpoint URL.
    /// </summary>
    public string MediaUrl => $"{ApiBaseUrl}/{PhoneNumberId}/media";

    /// <summary>
    /// Templates endpoint URL.
    /// </summary>
    public string TemplatesUrl => $"{ApiBaseUrl}/{WabaId}/message_templates";
}
