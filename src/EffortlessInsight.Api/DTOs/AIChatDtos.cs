namespace EffortlessInsight.Api.DTOs;

// =============================================================================
// CONVERSATION DTOs
// =============================================================================

public record ConversationDto(
    Guid Id,
    Guid NoticeId,
    string Title,
    string Status,
    int MessageCount,
    DateTime? LastMessageAt,
    DateTime CreatedAt,
    MessageDto? LastMessage
);

public record ConversationListDto(
    List<ConversationDto> Conversations,
    int TotalCount
);

public record ConversationDetailDto(
    Guid Id,
    Guid NoticeId,
    string Title,
    string Status,
    int MessageCount,
    int TotalTokens,
    DateTime CreatedAt,
    DateTime? LastMessageAt,
    List<MessageDto> Messages,
    bool HasMore,
    string? NextCursor
);

// =============================================================================
// MESSAGE DTOs
// =============================================================================

public record MessageDto(
    Guid Id,
    string Role,
    string Content,
    string? ContentHtml,
    List<CitationDto>? Citations,
    DateTime CreatedAt,
    int? TokenCount,
    string? ModelId,
    bool IsError,
    FeedbackDto? Feedback
);

public record MessageListDto(
    List<MessageDto> Messages,
    bool HasMore,
    string? NextCursor
);

public record CitationDto(
    string Source,
    string Reference,
    string? Quote
);

public record FeedbackDto(
    int Rating,
    string? FeedbackText
);

// =============================================================================
// REQUEST DTOs
// =============================================================================

public record CreateConversationRequest(
    string? Title
);

public record SendMessageRequest(
    string Message,
    bool Stream = true
);

public record RegenerateMessageRequest(
    Guid MessageId
);

public record MessageFeedbackRequest(
    int Rating,  // 1 or -1
    string? FeedbackText
);

// =============================================================================
// STREAMING DTOs
// =============================================================================

public record ChatStreamEvent(
    string Type,
    object? Data
);

public static class ChatEventType
{
    public const string UserMessageSaved = "user_message_saved";
    public const string StreamStarted = "stream_started";
    public const string ContentChunk = "content_chunk";
    public const string StreamCompleted = "stream_completed";
    public const string Error = "error";
}
