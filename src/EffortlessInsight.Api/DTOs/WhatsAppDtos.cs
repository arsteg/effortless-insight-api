using System.Text.Json.Serialization;

namespace EffortlessInsight.Api.DTOs;

#region Send Message Models

/// <summary>
/// Result of sending a WhatsApp message.
/// </summary>
public record WhatsAppSendResult(
    bool Success,
    string? MessageId,
    string? ErrorCode,
    string? ErrorMessage
);

/// <summary>
/// WhatsApp button for interactive messages.
/// </summary>
public record WhatsAppButton(
    string Id,
    string Title
);

/// <summary>
/// WhatsApp list section for interactive messages.
/// </summary>
public record WhatsAppListSection(
    string Title,
    List<WhatsAppListRow> Rows
);

/// <summary>
/// WhatsApp list row for interactive messages.
/// </summary>
public record WhatsAppListRow(
    string Id,
    string Title,
    string? Description = null
);

/// <summary>
/// Template parameter for template messages.
/// </summary>
public record TemplateParameter(
    string Type,
    object Value
);

/// <summary>
/// Template component for template messages.
/// </summary>
public record TemplateComponent(
    string Type,
    List<TemplateParameter> Parameters
);

#endregion

#region Incoming Message Models

/// <summary>
/// Incoming WhatsApp message from webhook.
/// </summary>
public record WhatsAppIncomingMessage(
    string WamId,
    string From,
    string PhoneNumberId,
    long Timestamp,
    string Type,
    string? Text,
    string? ButtonReplyId,
    string? ListReplyId,
    string? MediaId,
    string? Caption
);

#endregion

#region Meta API Request/Response Models

/// <summary>
/// Meta API text message request.
/// </summary>
public class MetaTextMessageRequest
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    [JsonPropertyName("recipient_type")]
    public string RecipientType { get; set; } = "individual";

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public MetaTextContent Text { get; set; } = new();
}

public class MetaTextContent
{
    [JsonPropertyName("preview_url")]
    public bool PreviewUrl { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}

/// <summary>
/// Meta API template message request.
/// </summary>
public class MetaTemplateMessageRequest
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "template";

    [JsonPropertyName("template")]
    public MetaTemplateContent Template { get; set; } = new();
}

public class MetaTemplateContent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public MetaLanguage Language { get; set; } = new();

    [JsonPropertyName("components")]
    public List<MetaTemplateComponent>? Components { get; set; }
}

public class MetaLanguage
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "en";
}

public class MetaTemplateComponent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<MetaTemplateParameter>? Parameters { get; set; }
}

public class MetaTemplateParameter
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image")]
    public MetaMediaObject? Image { get; set; }

    [JsonPropertyName("document")]
    public MetaMediaObject? Document { get; set; }
}

public class MetaMediaObject
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }
}

/// <summary>
/// Meta API interactive message request.
/// </summary>
public class MetaInteractiveMessageRequest
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    [JsonPropertyName("recipient_type")]
    public string RecipientType { get; set; } = "individual";

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "interactive";

    [JsonPropertyName("interactive")]
    public MetaInteractiveContent Interactive { get; set; } = new();
}

public class MetaInteractiveContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "button";

    [JsonPropertyName("header")]
    public MetaInteractiveHeader? Header { get; set; }

    [JsonPropertyName("body")]
    public MetaInteractiveBody Body { get; set; } = new();

    [JsonPropertyName("footer")]
    public MetaInteractiveFooter? Footer { get; set; }

    [JsonPropertyName("action")]
    public MetaInteractiveAction Action { get; set; } = new();
}

public class MetaInteractiveHeader
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class MetaInteractiveBody
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class MetaInteractiveFooter
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class MetaInteractiveAction
{
    [JsonPropertyName("buttons")]
    public List<MetaInteractiveButton>? Buttons { get; set; }

    [JsonPropertyName("button")]
    public string? Button { get; set; }

    [JsonPropertyName("sections")]
    public List<MetaListSection>? Sections { get; set; }
}

public class MetaInteractiveButton
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "reply";

    [JsonPropertyName("reply")]
    public MetaButtonReply Reply { get; set; } = new();
}

public class MetaButtonReply
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public class MetaListSection
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("rows")]
    public List<MetaListRow> Rows { get; set; } = [];
}

public class MetaListRow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Meta API message response.
/// </summary>
public class MetaMessageResponse
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("contacts")]
    public List<MetaContact>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<MetaMessageInfo>? Messages { get; set; }
}

public class MetaContact
{
    [JsonPropertyName("input")]
    public string? Input { get; set; }

    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }
}

public class MetaMessageInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("message_status")]
    public string? MessageStatus { get; set; }
}

/// <summary>
/// Meta API error response.
/// </summary>
public class MetaErrorResponse
{
    [JsonPropertyName("error")]
    public MetaError? Error { get; set; }
}

public class MetaError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("error_subcode")]
    public int? ErrorSubcode { get; set; }

    [JsonPropertyName("fbtrace_id")]
    public string? FbtraceId { get; set; }
}

#endregion

#region Webhook Payload Models

/// <summary>
/// Meta webhook payload.
/// </summary>
public class MetaWebhookPayload
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<MetaWebhookEntry>? Entry { get; set; }
}

public class MetaWebhookEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("changes")]
    public List<MetaWebhookChange>? Changes { get; set; }
}

public class MetaWebhookChange
{
    [JsonPropertyName("value")]
    public MetaWebhookValue? Value { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }
}

public class MetaWebhookValue
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("metadata")]
    public MetaWebhookMetadata? Metadata { get; set; }

    [JsonPropertyName("contacts")]
    public List<MetaWebhookContact>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<MetaWebhookMessage>? Messages { get; set; }

    [JsonPropertyName("statuses")]
    public List<MetaWebhookStatus>? Statuses { get; set; }

    [JsonPropertyName("errors")]
    public List<MetaWebhookError>? Errors { get; set; }
}

public class MetaWebhookMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; set; }
}

public class MetaWebhookContact
{
    [JsonPropertyName("profile")]
    public MetaWebhookProfile? Profile { get; set; }

    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }
}

public class MetaWebhookProfile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class MetaWebhookMessage
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public MetaWebhookText? Text { get; set; }

    [JsonPropertyName("interactive")]
    public MetaWebhookInteractive? Interactive { get; set; }

    [JsonPropertyName("image")]
    public MetaWebhookMedia? Image { get; set; }

    [JsonPropertyName("document")]
    public MetaWebhookMedia? Document { get; set; }

    [JsonPropertyName("button")]
    public MetaWebhookButtonReply? Button { get; set; }
}

public class MetaWebhookText
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

public class MetaWebhookInteractive
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("button_reply")]
    public MetaWebhookButtonReplyInfo? ButtonReply { get; set; }

    [JsonPropertyName("list_reply")]
    public MetaWebhookListReply? ListReply { get; set; }
}

public class MetaWebhookButtonReply
{
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class MetaWebhookButtonReplyInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

public class MetaWebhookListReply
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class MetaWebhookMedia
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }
}

public class MetaWebhookStatus
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; set; }

    [JsonPropertyName("errors")]
    public List<MetaWebhookError>? Errors { get; set; }
}

public class MetaWebhookError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error_data")]
    public MetaWebhookErrorData? ErrorData { get; set; }
}

public class MetaWebhookErrorData
{
    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

#endregion

#region Template Info Models

/// <summary>
/// WhatsApp template info from Meta API.
/// </summary>
public class WhatsAppTemplateInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("components")]
    public List<WhatsAppTemplateComponentInfo>? Components { get; set; }
}

public class WhatsAppTemplateComponentInfo
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("buttons")]
    public List<WhatsAppTemplateButtonInfo>? Buttons { get; set; }
}

public class WhatsAppTemplateButtonInfo
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }
}

public class MetaTemplatesResponse
{
    [JsonPropertyName("data")]
    public List<WhatsAppTemplateInfo>? Data { get; set; }

    [JsonPropertyName("paging")]
    public MetaPaging? Paging { get; set; }
}

public class MetaPaging
{
    [JsonPropertyName("cursors")]
    public MetaCursors? Cursors { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}

public class MetaCursors
{
    [JsonPropertyName("before")]
    public string? Before { get; set; }

    [JsonPropertyName("after")]
    public string? After { get; set; }
}

#endregion

#region API Request/Response DTOs

/// <summary>
/// Request to initiate WhatsApp linking from app.
/// </summary>
public record WhatsAppLinkRequest(
    string PhoneNumber
);

/// <summary>
/// Response for WhatsApp link initiation.
/// </summary>
public record WhatsAppLinkResponse(
    bool Success,
    DateTime? ExpiresAt,
    string? Message
);

/// <summary>
/// WhatsApp connection status response.
/// </summary>
public record WhatsAppStatusResponse(
    bool Linked,
    string? PhoneNumber,
    DateTime? LinkedAt,
    bool OptedIn,
    DateTime? LastMessageAt
);

/// <summary>
/// WhatsApp preferences update request.
/// </summary>
public record WhatsAppPreferencesRequest(
    bool? DeadlineReminders,
    bool? HighRiskAlerts,
    bool? TaskAssignments,
    bool? DailyDigest
);

/// <summary>
/// User WhatsApp notification preferences.
/// </summary>
public record WhatsAppPreferencesResponse(
    bool DeadlineReminders,
    bool HighRiskAlerts,
    bool TaskAssignments,
    bool DailyDigest
);

#endregion
