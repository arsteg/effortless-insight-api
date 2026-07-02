using System.Text.Json.Serialization;

namespace EffortlessInsight.Api.DTOs;

#region Notification DTOs

/// <summary>
/// Notification item for list/detail responses
/// </summary>
public record NotificationDto(
    Guid Id,
    string Type,
    string Category,
    string Priority,
    string Title,
    string Body,
    Dictionary<string, object> Data,
    string? ActionUrl,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt,
    NotificationDeliveryStatusDto? Channels
);

/// <summary>
/// Delivery status per channel
/// </summary>
public record NotificationDeliveryStatusDto(
    ChannelDeliveryDto? Email,
    ChannelDeliveryDto? Sms,
    ChannelDeliveryDto? Push,
    ChannelDeliveryDto? WhatsApp,
    ChannelDeliveryDto? InApp
);

/// <summary>
/// Delivery info for a single channel
/// </summary>
public record ChannelDeliveryDto(
    bool Sent,
    DateTime? DeliveredAt,
    DateTime? OpenedAt,
    string? Status
);

/// <summary>
/// Paginated notification list response
/// </summary>
public record NotificationListResponse(
    List<NotificationDto> Notifications,
    int UnreadCount,
    PaginationInfo Pagination
);

/// <summary>
/// Pagination metadata
/// </summary>
public record PaginationInfo(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages
);

/// <summary>
/// Mark notification as read response
/// </summary>
public record MarkReadResponse(
    Guid Id,
    bool Read,
    DateTime ReadAt
);

/// <summary>
/// Bulk mark read request
/// </summary>
public record MarkAllReadRequest(
    DateTime? BeforeDate,
    string? Type
);

/// <summary>
/// Bulk mark read response
/// </summary>
public record MarkAllReadResponse(
    int MarkedCount,
    int UnreadCount
);

#endregion

#region Notification Preferences DTOs

/// <summary>
/// User notification preferences response
/// </summary>
public record NotificationPreferencesDto
{
    public ChannelPreferencesDto Channels { get; init; } = new();
    public QuietHoursDto QuietHours { get; init; } = new();
    public Dictionary<string, TypeChannelPreferencesDto> Preferences { get; init; } = new();
    public DigestPreferencesDto Digest { get; init; } = new();
}

/// <summary>
/// Channel-specific settings
/// </summary>
public record ChannelPreferencesDto
{
    public EmailChannelDto Email { get; init; } = new();
    public SmsChannelDto Sms { get; init; } = new();
    public WhatsAppChannelDto WhatsApp { get; init; } = new();
    public PushChannelDto Push { get; init; } = new();
}

public record EmailChannelDto
{
    public bool Enabled { get; init; } = true;
    public string? Address { get; init; }
    public bool Verified { get; init; }
}

public record SmsChannelDto
{
    public bool Enabled { get; init; } = true;
    public string? Phone { get; init; }
    public bool Verified { get; init; }
}

public record WhatsAppChannelDto
{
    public bool Enabled { get; init; } = false;
    public string? Phone { get; init; }
    public bool Verified { get; init; }
}

public record PushChannelDto
{
    public bool Enabled { get; init; } = true;
    public List<PushTokenInfoDto> Tokens { get; init; } = new();
}

public record PushTokenInfoDto(
    string Platform,
    DateTime RegisteredAt
);

/// <summary>
/// Quiet hours configuration
/// </summary>
public record QuietHoursDto
{
    public bool Enabled { get; set; } = false;
    public string Start { get; set; } = "22:00";
    public string End { get; set; } = "07:00";
    public string Timezone { get; set; } = "Asia/Kolkata";
}

/// <summary>
/// Per-type channel preferences (which channels to use for each notification type)
/// </summary>
public record TypeChannelPreferencesDto
{
    public bool Email { get; init; } = true;
    public bool Sms { get; init; } = false;
    public bool Push { get; init; } = true;
    public bool WhatsApp { get; init; } = false;
    public bool InApp { get; init; } = true;
}

/// <summary>
/// Digest email preferences
/// </summary>
public record DigestPreferencesDto
{
    public DailyDigestDto Daily { get; set; } = new();
    public WeeklyDigestDto Weekly { get; set; } = new();
}

public record DailyDigestDto
{
    public bool Enabled { get; set; } = true;
    public string Time { get; set; } = "09:00";
    public string Timezone { get; set; } = "Asia/Kolkata";
}

public record WeeklyDigestDto
{
    public bool Enabled { get; set; } = false;
    public int Day { get; set; } = 1;  // Monday
    public string Time { get; set; } = "09:00";
}

/// <summary>
/// Update notification preferences request
/// </summary>
public record UpdatePreferencesRequest
{
    public UpdateChannelPreferencesDto? Channels { get; init; }
    public UpdateQuietHoursDto? QuietHours { get; init; }
    public Dictionary<string, TypeChannelPreferencesDto>? Preferences { get; init; }
    public UpdateDigestDto? Digest { get; init; }
}

public record UpdateChannelPreferencesDto
{
    public UpdateEmailChannelDto? Email { get; init; }
    public UpdateSmsChannelDto? Sms { get; init; }
    public UpdateWhatsAppChannelDto? WhatsApp { get; init; }
    public UpdatePushChannelDto? Push { get; init; }
}

public record UpdateEmailChannelDto
{
    public bool? Enabled { get; init; }
    public string? Address { get; init; }
}

public record UpdateSmsChannelDto
{
    public bool? Enabled { get; init; }
    public string? Phone { get; init; }
}

public record UpdateWhatsAppChannelDto
{
    public bool? Enabled { get; init; }
    public string? Phone { get; init; }
}

public record UpdatePushChannelDto
{
    public bool? Enabled { get; init; }
}

public record UpdateQuietHoursDto
{
    public bool? Enabled { get; init; }
    public string? Start { get; init; }
    public string? End { get; init; }
    public string? Timezone { get; init; }
}

public record UpdateDigestDto
{
    public UpdateDailyDigestDto? Daily { get; init; }
    public UpdateWeeklyDigestDto? Weekly { get; init; }
}

public record UpdateDailyDigestDto
{
    public bool? Enabled { get; init; }
    public string? Time { get; init; }
    public string? Timezone { get; init; }
}

public record UpdateWeeklyDigestDto
{
    public bool? Enabled { get; init; }
    public int? Day { get; init; }
    public string? Time { get; init; }
}

#endregion

#region Push Token DTOs

/// <summary>
/// Register push token request
/// </summary>
public record RegisterPushTokenRequest(
    string Token,
    string Platform,  // web, android, ios
    Dictionary<string, object>? DeviceInfo
);

/// <summary>
/// Push token response
/// </summary>
public record PushTokenDto(
    Guid Id,
    string Platform,
    DateTime RegisteredAt,
    DateTime? LastUsedAt,
    bool IsActive
);

#endregion

#region Internal Notification API DTOs

/// <summary>
/// Internal API: Send notification request
/// </summary>
public record SendNotificationRequest(
    Guid UserId,
    string Type,
    Dictionary<string, object> Data,
    DateTime? ScheduledFor = null,
    bool OverridePreferences = false
);

/// <summary>
/// Internal API: Send notification response
/// </summary>
public record SendNotificationResponse(
    Guid NotificationId,
    List<DeliveryResultDto> Deliveries
);

/// <summary>
/// Delivery result per channel
/// </summary>
public record DeliveryResultDto(
    string Channel,
    string Status,
    string? MessageId
);

/// <summary>
/// Internal API: Bulk send request
/// </summary>
public record BulkNotificationRequest(
    List<SingleNotificationRequest> Notifications,
    string? BatchId
);

/// <summary>
/// Single notification in bulk request
/// </summary>
public record SingleNotificationRequest(
    Guid UserId,
    string Type,
    Dictionary<string, object> Data
);

/// <summary>
/// Bulk send response
/// </summary>
public record BulkNotificationResponse(
    string BatchId,
    int TotalQueued,
    int TotalFailed,
    List<BulkResultItemDto> Results
);

/// <summary>
/// Individual result in bulk response
/// </summary>
public record BulkResultItemDto(
    Guid UserId,
    Guid? NotificationId,
    bool Success,
    string? Error
);

#endregion

#region Webhook DTOs

/// <summary>
/// SendGrid webhook event
/// </summary>
public record SendGridEvent
{
    [JsonPropertyName("sg_message_id")]
    public string? SgMessageId { get; init; }

    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

#endregion

#region Template DTOs

/// <summary>
/// Notification template response
/// </summary>
public record NotificationTemplateDto(
    Guid Id,
    string Type,
    string Channel,
    string Language,
    int Version,
    string? Subject,
    string Body,
    Dictionary<string, object> Metadata,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

/// <summary>
/// Create/Update template request
/// </summary>
public record UpsertTemplateRequest(
    string Type,
    string Channel,
    string Language,
    string? Subject,
    string Body,
    Dictionary<string, object>? Metadata
);

#endregion

#region Unsubscribe DTOs

/// <summary>
/// Unsubscribe request (from email link)
/// </summary>
public record UnsubscribeRequest(
    string Token,
    string? NotificationType,
    string? Reason
);

/// <summary>
/// Unsubscribe response
/// </summary>
public record UnsubscribeResponse(
    bool Success,
    string Message
);

#endregion

#region Analytics DTOs

/// <summary>
/// Notification metrics response
/// </summary>
public record NotificationMetricsDto(
    Dictionary<string, ChannelMetricsDto> ByChannel,
    Dictionary<string, TypeMetricsDto> ByType,
    int TotalSent,
    int TotalDelivered,
    int TotalFailed,
    double OverallDeliveryRate,
    double OverallOpenRate,
    double OverallClickRate
);

/// <summary>
/// Metrics for a specific channel
/// </summary>
public record ChannelMetricsDto(
    int Sent,
    int Delivered,
    int Opened,
    int Clicked,
    int Failed,
    int Bounced,
    double DeliveryRate,
    double OpenRate,
    double ClickRate,
    double BounceRate,
    double AvgDeliveryTimeMs
);

/// <summary>
/// Metrics for a specific notification type
/// </summary>
public record TypeMetricsDto(
    int Sent,
    int Delivered,
    int Opened,
    int Clicked,
    double EngagementRate
);

#endregion
