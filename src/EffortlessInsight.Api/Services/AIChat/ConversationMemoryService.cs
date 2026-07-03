using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.AIChat.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Manages conversation memory, including summarization and token optimization.
/// </summary>
public class ConversationMemoryService : IConversationMemoryService
{
    private readonly ApplicationDbContext _db;
    private readonly IAIProvider _aiProvider;
    private readonly AIChatOptions _options;
    private readonly ILogger<ConversationMemoryService> _logger;

    private const string SUMMARIZATION_PROMPT = """
        You are summarizing a conversation about a GST notice for context preservation.

        Summarize the key points of the following conversation:
        1. Main questions asked by the user
        2. Key information provided in the responses
        3. Any decisions or conclusions reached
        4. Outstanding questions or action items

        Keep the summary concise (under 500 words) but preserve important details.
        Focus on facts and information that would be relevant for future questions.

        Conversation:
        {CONVERSATION}
        """;

    public ConversationMemoryService(
        ApplicationDbContext db,
        IAIProvider aiProvider,
        IOptions<AIChatOptions> options,
        ILogger<ConversationMemoryService> logger)
    {
        _db = db;
        _aiProvider = aiProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SummarizeConversationAsync(
        NoticeConversation conversation,
        List<NoticeMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return string.Empty;

        // Build conversation text
        var conversationText = new StringBuilder();
        foreach (var msg in messages)
        {
            var role = msg.Role == MessageRole.User ? "User" : "Assistant";
            conversationText.AppendLine($"{role}: {msg.Content}");
            conversationText.AppendLine();
        }

        // Generate summary using AI
        var prompt = SUMMARIZATION_PROMPT.Replace("{CONVERSATION}", conversationText.ToString());

        var request = new AICompletionRequest(
            SystemPrompt: "You are a helpful assistant that creates concise summaries.",
            Messages: [new AIMessage("user", prompt)],
            MaxTokens: 1000,
            Temperature: 0.3f
        );

        var result = await _aiProvider.CompleteAsync(request, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("Failed to generate conversation summary: {Error}", result.ErrorMessage);
            return GenerateFallbackSummary(messages);
        }

        return result.Content;
    }

    public async Task<ConversationSummary> CreateSummaryAsync(
        Guid conversationId,
        List<NoticeMessage> messagesToSummarize,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _db.NoticeConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation == null)
            throw new KeyNotFoundException($"Conversation {conversationId} not found");

        // Generate summary
        var summaryText = await SummarizeConversationAsync(conversation, messagesToSummarize, cancellationToken);

        var lastMessage = messagesToSummarize.Last();

        var summary = new ConversationSummary
        {
            ConversationId = conversationId,
            Summary = summaryText,
            CoveredMessageCount = messagesToSummarize.Count,
            LastMessageId = lastMessage.Id,
            TokenCount = EstimateTokens(summaryText)
        };

        _db.ConversationSummaries.Add(summary);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created summary for conversation {ConversationId} covering {MessageCount} messages",
            conversationId, messagesToSummarize.Count);

        return summary;
    }

    public bool NeedsSummarization(NoticeConversation conversation, int maxMessages, int maxTokens)
    {
        // Check message count
        if (conversation.MessageCount > maxMessages)
            return true;

        // Check token usage
        if (conversation.TotalTokens > maxTokens)
            return true;

        return false;
    }

    public async Task<OptimizedHistory> GetOptimizedHistoryAsync(
        Guid conversationId,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        // Get existing summary
        var existingSummary = await _db.ConversationSummaries
            .Where(s => s.ConversationId == conversationId)
            .Where(s => s.DeletedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Get messages after the summary (or all if no summary)
        var messagesQuery = _db.NoticeMessages
            .Where(m => m.ConversationId == conversationId)
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.CreatedAt);

        List<NoticeMessage> recentMessages;
        int summarizedCount = 0;

        if (existingSummary != null)
        {
            // Get messages after the last summarized message
            var lastSummarizedMessage = await _db.NoticeMessages
                .FirstOrDefaultAsync(m => m.Id == existingSummary.LastMessageId, cancellationToken);

            if (lastSummarizedMessage != null)
            {
                recentMessages = await messagesQuery
                    .Where(m => m.CreatedAt > lastSummarizedMessage.CreatedAt)
                    .ToListAsync(cancellationToken);
                summarizedCount = existingSummary.CoveredMessageCount;
            }
            else
            {
                recentMessages = await messagesQuery.ToListAsync(cancellationToken);
            }
        }
        else
        {
            recentMessages = await messagesQuery.ToListAsync(cancellationToken);
        }

        // Check if we need to create a new summary
        var conversation = await _db.NoticeConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation != null &&
            NeedsSummarization(conversation, _options.Conversation.SummarizeAfterMessages, maxTokens) &&
            recentMessages.Count > _options.Conversation.MaxMessagesInContext)
        {
            // Need to summarize older messages from recentMessages
            var messagesToSummarize = recentMessages
                .Take(recentMessages.Count - _options.Conversation.MaxMessagesInContext / 2)
                .ToList();

            if (messagesToSummarize.Count >= 4) // Only summarize if we have enough messages
            {
                var newSummary = await CreateSummaryAsync(
                    conversationId, messagesToSummarize, cancellationToken);

                // Update recent messages to exclude summarized ones
                recentMessages = recentMessages
                    .Skip(messagesToSummarize.Count)
                    .ToList();

                existingSummary = newSummary;
                summarizedCount += messagesToSummarize.Count;
            }
        }

        // Calculate budget for recent messages
        var summaryTokens = existingSummary != null ? existingSummary.TokenCount : 0;
        var messagesBudget = maxTokens - summaryTokens;

        // Trim messages to fit budget
        var optimizedMessages = GetMessagesWithinBudget(recentMessages, messagesBudget);
        var totalTokens = summaryTokens + optimizedMessages.Sum(m => m.TokenCount ?? EstimateTokens(m.Content));

        return new OptimizedHistory(
            existingSummary?.Summary,
            optimizedMessages,
            totalTokens,
            summarizedCount
        );
    }

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private List<NoticeMessage> GetMessagesWithinBudget(List<NoticeMessage> messages, int maxTokens)
    {
        var result = new List<NoticeMessage>();
        var currentTokens = 0;

        // Always include the most recent messages first
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var tokens = msg.TokenCount ?? EstimateTokens(msg.Content);

            if (currentTokens + tokens > maxTokens && result.Count > 0)
                break;

            result.Insert(0, msg);
            currentTokens += tokens;
        }

        return result;
    }

    private static string GenerateFallbackSummary(List<NoticeMessage> messages)
    {
        // Simple fallback if AI summarization fails
        var sb = new StringBuilder();
        sb.AppendLine("Previous conversation covered:");

        var userMessages = messages.Where(m => m.Role == MessageRole.User).ToList();
        if (userMessages.Any())
        {
            sb.AppendLine($"- {userMessages.Count} user questions");
            var firstQuestion = userMessages.First().Content;
            if (firstQuestion.Length > 100)
                firstQuestion = firstQuestion[..100] + "...";
            sb.AppendLine($"- First question: \"{firstQuestion}\"");
        }

        return sb.ToString();
    }
}
