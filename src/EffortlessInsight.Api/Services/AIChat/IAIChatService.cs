using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Main orchestration service for AI chat functionality.
/// </summary>
public interface IAIChatService
{
    /// <summary>
    /// Send a message and get AI response (non-streaming).
    /// </summary>
    Task<MessageDto> SendMessageAsync(
        Guid conversationId,
        Guid userId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message and stream the AI response.
    /// </summary>
    IAsyncEnumerable<ChatStreamEvent> StreamMessageAsync(
        Guid conversationId,
        Guid userId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a previously sent user message: rewinds the conversation to that point,
    /// re-sends the edited content, and streams a new AI response.
    /// </summary>
    IAsyncEnumerable<ChatStreamEvent> EditMessageStreamAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerate the last assistant message.
    /// </summary>
    Task<MessageDto> RegenerateMessageAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get suggested questions for a notice.
    /// </summary>
    Task<List<string>> GetSuggestedQuestionsAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);
}
