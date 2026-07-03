using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Service for retrieving and managing context for AI chat conversations.
/// </summary>
public interface IContextRetrievalService
{
    /// <summary>
    /// Get the full context for a conversation, including notice, AI report, messages, and summary.
    /// </summary>
    Task<ChatContext> GetContextAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the context for a notice (before conversation exists).
    /// </summary>
    Task<NoticeContext> GetNoticeContextAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate the total token count for the current context.
    /// </summary>
    int EstimateContextTokens(ChatContext context);

    /// <summary>
    /// Check if the context exceeds the token limit and needs summarization.
    /// </summary>
    bool NeedsSummarization(ChatContext context, int maxTokens);
}

/// <summary>
/// Contains all the context needed for AI chat, with token estimates.
/// </summary>
public record ChatContext(
    Notice Notice,
    NoticeAiReport? AiReport,
    NoticeConversation Conversation,
    List<NoticeMessage> Messages,
    string? Summary,
    int EstimatedTokens
)
{
    /// <summary>
    /// Get messages within a token budget, starting from the most recent.
    /// </summary>
    public List<NoticeMessage> GetMessagesWithinBudget(int maxTokens)
    {
        var result = new List<NoticeMessage>();
        var currentTokens = 0;

        // Start from the most recent message and work backwards
        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            var msg = Messages[i];
            var tokens = msg.TokenCount ?? EstimateTokens(msg.Content);

            if (currentTokens + tokens > maxTokens)
                break;

            result.Insert(0, msg);
            currentTokens += tokens;
        }

        return result;
    }

    private static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / 4.0);
}

/// <summary>
/// Context for a notice before any conversation exists.
/// </summary>
public record NoticeContext(
    Notice Notice,
    NoticeAiReport? AiReport,
    int EstimatedNoticeTokens,
    int EstimatedReportTokens
);
