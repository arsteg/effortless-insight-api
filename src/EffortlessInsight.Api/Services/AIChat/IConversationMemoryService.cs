using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Service for managing conversation memory, including summarization and token optimization.
/// </summary>
public interface IConversationMemoryService
{
    /// <summary>
    /// Generate a summary of the conversation history.
    /// </summary>
    Task<string> SummarizeConversationAsync(
        NoticeConversation conversation,
        List<NoticeMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create and save a conversation summary.
    /// </summary>
    Task<ConversationSummary> CreateSummaryAsync(
        Guid conversationId,
        List<NoticeMessage> messagesToSummarize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if conversation needs summarization based on message count or token usage.
    /// </summary>
    bool NeedsSummarization(NoticeConversation conversation, int maxMessages, int maxTokens);

    /// <summary>
    /// Get optimized message history that fits within token budget.
    /// Returns recent messages plus summary if needed.
    /// </summary>
    Task<OptimizedHistory> GetOptimizedHistoryAsync(
        Guid conversationId,
        int maxTokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate token count for a piece of text.
    /// </summary>
    int EstimateTokens(string text);
}

/// <summary>
/// Optimized conversation history that fits within token limits.
/// </summary>
public record OptimizedHistory(
    string? Summary,
    List<NoticeMessage> RecentMessages,
    int TotalTokens,
    int SummarizedMessageCount
);
