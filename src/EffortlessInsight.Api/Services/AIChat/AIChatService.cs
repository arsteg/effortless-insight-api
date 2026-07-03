using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.AIChat.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Main orchestration service for AI chat functionality.
/// </summary>
public class AIChatService : IAIChatService
{
    private readonly IConversationService _conversationService;
    private readonly IPromptBuilderService _promptBuilder;
    private readonly IContextRetrievalService _contextRetrieval;
    private readonly IConversationMemoryService _memoryService;
    private readonly IAIProvider _aiProvider;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly AIChatOptions _options;
    private readonly ILogger<AIChatService> _logger;

    public AIChatService(
        IConversationService conversationService,
        IPromptBuilderService promptBuilder,
        IContextRetrievalService contextRetrieval,
        IConversationMemoryService memoryService,
        IAIProvider aiProvider,
        ApplicationDbContext db,
        ITenantContext tenantContext,
        IOptions<AIChatOptions> options,
        ILogger<AIChatService> logger)
    {
        _conversationService = conversationService;
        _promptBuilder = promptBuilder;
        _contextRetrieval = contextRetrieval;
        _memoryService = memoryService;
        _aiProvider = aiProvider;
        _db = db;
        _tenantContext = tenantContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MessageDto> SendMessageAsync(
        Guid conversationId,
        Guid userId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Add user message
        var userMessage = await _conversationService.AddUserMessageAsync(
            conversationId, userId, request.Message, cancellationToken);

        // 2. Get optimized context using memory service
        var context = await _contextRetrieval.GetContextAsync(conversationId, cancellationToken);
        var optimizedHistory = await _memoryService.GetOptimizedHistoryAsync(
            conversationId,
            _options.Conversation.MaxTokensForContext,
            cancellationToken);

        // 3. Build prompt
        var systemPrompt = _promptBuilder.BuildSystemPrompt(context.Notice, context.AiReport);
        var conversationMessages = _promptBuilder.BuildConversationContext(
            context.Notice,
            context.AiReport,
            optimizedHistory.RecentMessages,
            optimizedHistory.Summary,
            _options.Conversation.MaxTokensForContext);

        // 4. Calculate available tokens for response
        var contextTokens = TokenManager.EstimateConversationTokens(
            systemPrompt,
            conversationMessages.Select(m => (m.Role, m.Content)));
        var maxResponseTokens = Math.Min(
            _options.OpenAI.MaxTokensPerRequest,
            TokenManager.CalculateAvailableResponseTokens(_aiProvider.DefaultModel, contextTokens));

        // 5. Call AI
        var aiRequest = new AICompletionRequest(
            systemPrompt,
            conversationMessages,
            Temperature: _options.OpenAI.Temperature,
            MaxTokens: maxResponseTokens);

        var result = await _aiProvider.CompleteAsync(aiRequest, cancellationToken);

        // 6. Save assistant message
        var citations = ExtractCitations(result.Content);
        var assistantMessage = await _conversationService.AddAssistantMessageAsync(
            conversationId, result, citations, cancellationToken);

        // 7. Log audit
        await LogAuditAsync(context.Conversation, assistantMessage.Id, userId, result, cancellationToken);

        // 8. Check if summarization is needed for future requests
        await CheckAndSummarizeIfNeededAsync(context.Conversation, cancellationToken);

        return assistantMessage;
    }

    public async IAsyncEnumerable<ChatStreamEvent> StreamMessageAsync(
        Guid conversationId,
        Guid userId,
        SendMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. Add user message
        var userMessage = await _conversationService.AddUserMessageAsync(
            conversationId, userId, request.Message, cancellationToken);

        yield return new ChatStreamEvent(ChatEventType.UserMessageSaved, userMessage);

        // 2. Get optimized context
        var context = await _contextRetrieval.GetContextAsync(conversationId, cancellationToken);
        var optimizedHistory = await _memoryService.GetOptimizedHistoryAsync(
            conversationId,
            _options.Conversation.MaxTokensForContext,
            cancellationToken);

        // 3. Build prompt
        var systemPrompt = _promptBuilder.BuildSystemPrompt(context.Notice, context.AiReport);
        var conversationMessages = _promptBuilder.BuildConversationContext(
            context.Notice,
            context.AiReport,
            optimizedHistory.RecentMessages,
            optimizedHistory.Summary,
            _options.Conversation.MaxTokensForContext);

        // Add current user message
        conversationMessages.Add(new AIMessage("user", request.Message));

        // 4. Calculate token budget
        var contextTokens = TokenManager.EstimateConversationTokens(
            systemPrompt,
            conversationMessages.Select(m => (m.Role, m.Content)));
        var maxResponseTokens = Math.Min(
            _options.OpenAI.MaxTokensPerRequest,
            TokenManager.CalculateAvailableResponseTokens(_aiProvider.DefaultModel, contextTokens));

        _logger.LogDebug(
            "Streaming response for conversation {ConversationId}: {ContextTokens} context tokens, {MaxResponse} max response",
            conversationId, contextTokens, maxResponseTokens);

        // 5. Stream AI response
        var aiRequest = new AICompletionRequest(
            systemPrompt,
            conversationMessages,
            Temperature: _options.OpenAI.Temperature,
            MaxTokens: maxResponseTokens);

        var contentBuilder = new StringBuilder();
        var startTime = Stopwatch.StartNew();

        yield return new ChatStreamEvent(ChatEventType.StreamStarted, null);

        await foreach (var chunk in _aiProvider.StreamCompletionAsync(aiRequest, cancellationToken))
        {
            if (chunk.IsComplete)
            {
                // 6. Save completed message
                startTime.Stop();

                var outputTokens = TokenManager.EstimateTokens(contentBuilder.ToString());
                var result = new AICompletionResult(
                    Success: true,
                    Content: contentBuilder.ToString(),
                    InputTokens: contextTokens,
                    OutputTokens: outputTokens,
                    TotalTokens: contextTokens + outputTokens,
                    Model: _aiProvider.DefaultModel,
                    ResponseTimeMs: (int)startTime.ElapsedMilliseconds
                );

                var citations = ExtractCitations(contentBuilder.ToString());
                var assistantMessage = await _conversationService.AddAssistantMessageAsync(
                    conversationId, result, citations, cancellationToken);

                // Log audit
                await LogAuditAsync(context.Conversation, assistantMessage.Id, userId, result, cancellationToken);

                // Check if summarization is needed
                await CheckAndSummarizeIfNeededAsync(context.Conversation, cancellationToken);

                yield return new ChatStreamEvent(ChatEventType.StreamCompleted, assistantMessage);
            }
            else if (!string.IsNullOrEmpty(chunk.Content))
            {
                contentBuilder.Append(chunk.Content);
                yield return new ChatStreamEvent(ChatEventType.ContentChunk, chunk.Content);
            }
            else if (!string.IsNullOrEmpty(chunk.ErrorMessage))
            {
                yield return new ChatStreamEvent(ChatEventType.Error, chunk.ErrorMessage);
            }
        }
    }

    public async Task<MessageDto> RegenerateMessageAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Get the context
        var context = await _contextRetrieval.GetContextAsync(conversationId, cancellationToken);

        // Find the message to regenerate and the preceding user message
        var messageIndex = context.Messages.FindIndex(m => m.Id == messageId);
        if (messageIndex < 0 || context.Messages[messageIndex].Role != MessageRole.Assistant)
            throw new InvalidOperationException("Cannot regenerate this message");

        // Get messages up to (but not including) the message being regenerated
        var messagesUpTo = context.Messages.Take(messageIndex).ToList();

        // Find the last user message
        var lastUserMessage = messagesUpTo.LastOrDefault(m => m.Role == MessageRole.User);
        if (lastUserMessage == null)
            throw new InvalidOperationException("No user message found to regenerate from");

        // Build prompt with messages up to the user message
        var systemPrompt = _promptBuilder.BuildSystemPrompt(context.Notice, context.AiReport);
        var conversationMessages = _promptBuilder.BuildConversationContext(
            context.Notice,
            context.AiReport,
            messagesUpTo,
            null, // Don't use summary when regenerating
            _options.Conversation.MaxTokensForContext);

        // Call AI
        var aiRequest = new AICompletionRequest(
            systemPrompt,
            conversationMessages,
            Temperature: _options.OpenAI.Temperature + 0.1f, // Slightly higher temperature for variety
            MaxTokens: _options.OpenAI.MaxTokensPerRequest);

        var result = await _aiProvider.CompleteAsync(aiRequest, cancellationToken);

        // Save as a new message (don't delete the old one)
        var citations = ExtractCitations(result.Content);
        var newMessage = await _conversationService.AddAssistantMessageAsync(
            conversationId, result, citations, cancellationToken);

        await LogAuditAsync(context.Conversation, newMessage.Id, userId, result, cancellationToken);

        return newMessage;
    }

    public async Task<List<string>> GetSuggestedQuestionsAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var noticeContext = await _contextRetrieval.GetNoticeContextAsync(noticeId, cancellationToken);
            return _promptBuilder.GenerateSuggestedQuestions(noticeContext.Notice, noticeContext.AiReport);
        }
        catch (KeyNotFoundException)
        {
            return [];
        }
    }

    private async Task CheckAndSummarizeIfNeededAsync(
        NoticeConversation conversation,
        CancellationToken cancellationToken)
    {
        try
        {
            // Reload conversation to get current stats
            var currentConversation = await _db.NoticeConversations
                .FirstOrDefaultAsync(c => c.Id == conversation.Id, cancellationToken);

            if (currentConversation == null) return;

            // Check if summarization is needed
            if (_memoryService.NeedsSummarization(
                currentConversation,
                _options.Conversation.SummarizeAfterMessages,
                _options.Conversation.MaxTokensForContext))
            {
                _logger.LogInformation(
                    "Conversation {ConversationId} needs summarization (messages: {Count}, tokens: {Tokens})",
                    conversation.Id, currentConversation.MessageCount, currentConversation.TotalTokens);

                // Summarization will be handled by the memory service on next request
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking summarization need for conversation {ConversationId}", conversation.Id);
        }
    }

    private static List<Citation>? ExtractCitations(string content)
    {
        var citations = new List<Citation>();

        // Notice citations
        if (content.Contains("According to the notice", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("The notice states", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Notice mentions", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("as stated in the notice", StringComparison.OrdinalIgnoreCase))
        {
            citations.Add(new Citation("notice", "document", null));
        }

        // Analysis citations
        if (content.Contains("The analysis indicates", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Based on the analysis", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("AI analysis suggests", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("analysis shows", StringComparison.OrdinalIgnoreCase))
        {
            citations.Add(new Citation("analysis", "ai_report", null));
        }

        // Previous conversation citations
        if (content.Contains("As mentioned earlier", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("As we discussed", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("earlier in our conversation", StringComparison.OrdinalIgnoreCase))
        {
            citations.Add(new Citation("conversation", "previous_message", null));
        }

        return citations.Count > 0 ? citations : null;
    }

    private async Task LogAuditAsync(
        NoticeConversation conversation,
        Guid messageId,
        Guid userId,
        AICompletionResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var audit = new AIAuditLog
            {
                ConversationId = conversation.Id,
                MessageId = messageId,
                UserId = userId,
                OrganizationId = conversation.OrganizationId,
                ModelId = result.Model,
                PromptVersion = _promptBuilder.PromptVersion,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                TotalTokens = result.TotalTokens,
                EstimatedCost = TokenManager.EstimateCost(result.Model, result.InputTokens, result.OutputTokens),
                ResponseTimeMs = result.ResponseTimeMs,
                Status = result.Success ? AIAuditStatus.Success : AIAuditStatus.Error,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };

            _db.AIAuditLogs.Add(audit);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log AI audit for conversation {ConversationId}", conversation.Id);
        }
    }
}
