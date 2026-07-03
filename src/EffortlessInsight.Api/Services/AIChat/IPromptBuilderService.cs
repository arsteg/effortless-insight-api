using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.AIChat.Providers;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Service for building AI prompts with notice context.
/// </summary>
public interface IPromptBuilderService
{
    /// <summary>
    /// Build the system prompt with notice and analysis context.
    /// </summary>
    string BuildSystemPrompt(Notice notice, NoticeAiReport? aiReport);

    /// <summary>
    /// Build the conversation context (message history) respecting token limits.
    /// </summary>
    List<AIMessage> BuildConversationContext(
        Notice notice,
        NoticeAiReport? aiReport,
        List<NoticeMessage> messages,
        string? summary,
        int maxTokens);

    /// <summary>
    /// Generate a title for a conversation based on the first message.
    /// </summary>
    string GenerateConversationTitle(string firstMessage);

    /// <summary>
    /// Generate suggested questions based on the notice content.
    /// </summary>
    List<string> GenerateSuggestedQuestions(Notice notice, NoticeAiReport? aiReport);

    /// <summary>
    /// Get the current prompt version.
    /// </summary>
    string PromptVersion { get; }
}
