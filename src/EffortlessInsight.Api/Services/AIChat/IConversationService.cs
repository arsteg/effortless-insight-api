using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.AIChat.Providers;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Service for managing AI chat conversations.
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// Create a new conversation for a notice.
    /// </summary>
    Task<ConversationDetailDto> CreateConversationAsync(
        Guid noticeId,
        Guid userId,
        CreateConversationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all conversations for a notice.
    /// </summary>
    Task<ConversationListDto> GetConversationsAsync(
        Guid noticeId,
        Guid userId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific conversation with messages.
    /// </summary>
    Task<ConversationDetailDto?> GetConversationAsync(
        Guid conversationId,
        Guid userId,
        int messageLimit = 50,
        string? cursor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a conversation's title.
    /// </summary>
    Task<ConversationDto?> UpdateTitleAsync(
        Guid conversationId,
        Guid userId,
        string title,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a conversation.
    /// </summary>
    Task<bool> DeleteConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a user message and everything after it, rewinding the conversation
    /// so the message can be re-sent with edited content.
    /// </summary>
    Task TruncateFromMessageAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a user message to a conversation.
    /// </summary>
    Task<MessageDto> AddUserMessageAsync(
        Guid conversationId,
        Guid userId,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add an assistant message to a conversation.
    /// </summary>
    Task<MessageDto> AddAssistantMessageAsync(
        Guid conversationId,
        AICompletionResult result,
        List<Citation>? citations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archive a conversation.
    /// </summary>
    Task<bool> ArchiveConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add feedback for a message.
    /// </summary>
    Task AddFeedbackAsync(
        Guid messageId,
        Guid userId,
        MessageFeedbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the context for a conversation (notice, messages, etc.).
    /// </summary>
    Task<ConversationContext> GetConversationContextAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Contains all the context needed for AI chat.
/// </summary>
public record ConversationContext(
    Notice Notice,
    NoticeAiReport? AiReport,
    NoticeConversation Conversation,
    List<NoticeMessage> Messages,
    string? Summary
);
