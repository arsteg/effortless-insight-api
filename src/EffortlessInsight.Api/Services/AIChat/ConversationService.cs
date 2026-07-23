using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.AIChat.Providers;
using Markdig;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Implementation of conversation management for AI chat.
/// </summary>
public class ConversationService : IConversationService
{
    private readonly ApplicationDbContext _db;
    private readonly IPromptBuilderService _promptBuilder;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ConversationService> _logger;
    private readonly MarkdownPipeline _markdownPipeline;

    public ConversationService(
        ApplicationDbContext db,
        IPromptBuilderService promptBuilder,
        ITenantContext tenantContext,
        ILogger<ConversationService> logger)
    {
        _db = db;
        _promptBuilder = promptBuilder;
        _tenantContext = tenantContext;
        _logger = logger;
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
    }

    public async Task<ConversationDetailDto> CreateConversationAsync(
        Guid noticeId,
        Guid userId,
        CreateConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify notice exists and user has access
        var notice = await _db.Notices
            .Where(n => n.Id == noticeId)
            .Where(n => n.OrganizationId == _tenantContext.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (notice == null)
            throw new KeyNotFoundException("Notice not found");

        var conversation = new NoticeConversation
        {
            NoticeId = noticeId,
            UserId = userId,
            OrganizationId = _tenantContext.OrganizationId!.Value,
            Title = request.Title ?? "New Conversation",
            Status = ConversationStatus.Active
        };

        _db.NoticeConversations.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created conversation {ConversationId} for notice {NoticeId}",
            conversation.Id, noticeId);

        return MapToDetailDto(conversation, []);
    }

    public async Task<ConversationListDto> GetConversationsAsync(
        Guid noticeId,
        Guid userId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _db.NoticeConversations
            .Where(c => c.NoticeId == noticeId)
            .Where(c => c.OrganizationId == _tenantContext.OrganizationId)
            .Where(c => c.UserId == userId)
            .Where(c => c.DeletedAt == null)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var conversations = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
            .ToListAsync(cancellationToken);

        return new ConversationListDto(
            conversations.Select(c => MapToDto(c, c.Messages.FirstOrDefault())).ToList(),
            totalCount
        );
    }

    public async Task<ConversationDetailDto?> GetConversationAsync(
        Guid conversationId,
        Guid userId,
        int messageLimit = 50,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _db.NoticeConversations
            .Where(c => c.Id == conversationId)
            .Where(c => c.OrganizationId == _tenantContext.OrganizationId)
            .Where(c => c.UserId == userId || HasOrgAdminAccess(userId))
            .Where(c => c.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation == null)
            return null;

        // Get messages with pagination
        var messagesQuery = _db.NoticeMessages
            .Where(m => m.ConversationId == conversationId)
            .Include(m => m.Feedbacks)
            .OrderByDescending(m => m.CreatedAt);

        // Handle cursor-based pagination
        if (!string.IsNullOrEmpty(cursor) && Guid.TryParse(cursor, out var cursorId))
        {
            var cursorMessage = await _db.NoticeMessages
                .Where(m => m.Id == cursorId)
                .Select(m => m.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (cursorMessage != default)
            {
                messagesQuery = (IOrderedQueryable<NoticeMessage>)messagesQuery
                    .Where(m => m.CreatedAt < cursorMessage);
            }
        }

        var messages = await messagesQuery
            .Take(messageLimit + 1) // Get one extra to check if there's more
            .ToListAsync(cancellationToken);

        var hasMore = messages.Count > messageLimit;
        if (hasMore)
        {
            messages = messages.Take(messageLimit).ToList();
        }

        // Reverse to get chronological order
        messages.Reverse();

        var nextCursor = hasMore && messages.Count > 0
            ? messages.First().Id.ToString()
            : null;

        return MapToDetailDto(conversation, messages, hasMore, nextCursor);
    }

    public async Task<ConversationDto?> UpdateTitleAsync(
        Guid conversationId,
        Guid userId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetConversationEntityAsync(conversationId, userId, cancellationToken);
        if (conversation == null)
            return null;

        conversation.Title = title;
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(conversation, null);
    }

    public async Task<bool> DeleteConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetConversationEntityAsync(conversationId, userId, cancellationToken);
        if (conversation == null)
            return false;

        conversation.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted conversation {ConversationId}", conversationId);
        return true;
    }

    public async Task TruncateFromMessageAsync(
        Guid conversationId,
        Guid messageId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetConversationEntityAsync(conversationId, userId, cancellationToken);
        if (conversation == null)
            throw new KeyNotFoundException("Conversation not found");

        var target = await _db.NoticeMessages
            .Where(m => m.Id == messageId && m.ConversationId == conversationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (target == null)
            throw new KeyNotFoundException("Message not found");

        if (target.Role != MessageRole.User)
            throw new InvalidOperationException("Only user messages can be edited");

        // The edited message and everything after it get removed; the caller
        // re-sends the edited content as a new message.
        var toRemove = await _db.NoticeMessages
            .Where(m => m.ConversationId == conversationId)
            .Where(m => m.CreatedAt > target.CreatedAt || m.Id == target.Id)
            .ToListAsync(cancellationToken);

        // Audit logs reference messages without cascade delete — detach them so the
        // audit trail survives the truncation. (Feedback and summaries cascade.)
        var removedIds = toRemove.Select(m => m.Id).ToList();
        var auditLogs = await _db.AIAuditLogs
            .Where(a => a.MessageId != null && removedIds.Contains(a.MessageId.Value))
            .ToListAsync(cancellationToken);
        foreach (var log in auditLogs)
        {
            log.MessageId = null;
        }

        _db.NoticeMessages.RemoveRange(toRemove);

        // Rewind conversation stats
        conversation.MessageCount = Math.Max(0, conversation.MessageCount - toRemove.Count);
        conversation.TotalTokens = Math.Max(0, conversation.TotalTokens - toRemove.Sum(m => m.TokenCount ?? 0));
        conversation.LastMessageAt = await _db.NoticeMessages
            .Where(m => m.ConversationId == conversationId)
            .Where(m => !removedIds.Contains(m.Id))
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (DateTime?)m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Truncated {Count} messages from conversation {ConversationId} for edit-and-rewind",
            toRemove.Count, conversationId);
    }

    public async Task<MessageDto> AddUserMessageAsync(
        Guid conversationId,
        Guid userId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetConversationEntityAsync(conversationId, userId, cancellationToken);
        if (conversation == null)
            throw new KeyNotFoundException("Conversation not found");

        var message = new NoticeMessage
        {
            ConversationId = conversationId,
            Role = MessageRole.User,
            Content = content,
            TokenCount = EstimateTokens(content)
        };

        _db.NoticeMessages.Add(message);

        // Update conversation stats
        conversation.MessageCount++;
        conversation.TotalTokens += message.TokenCount ?? 0;
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;

        // Update title if this is the first message
        if (conversation.MessageCount == 1)
        {
            conversation.Title = _promptBuilder.GenerateConversationTitle(content);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return MapToMessageDto(message);
    }

    public async Task<MessageDto> AddAssistantMessageAsync(
        Guid conversationId,
        AICompletionResult result,
        List<Citation>? citations,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _db.NoticeConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation == null)
            throw new KeyNotFoundException("Conversation not found");

        var message = new NoticeMessage
        {
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = result.Content,
            ContentHtml = RenderMarkdown(result.Content),
            TokenCount = result.OutputTokens,
            ModelId = result.Model,
            PromptVersion = _promptBuilder.PromptVersion,
            ResponseTimeMs = result.ResponseTimeMs,
            Citations = citations,
            IsError = !result.Success,
            ErrorMessage = result.ErrorMessage
        };

        _db.NoticeMessages.Add(message);

        // Update conversation stats
        conversation.MessageCount++;
        conversation.TotalTokens += result.TotalTokens;
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return MapToMessageDto(message);
    }

    public async Task<bool> ArchiveConversationAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await GetConversationEntityAsync(conversationId, userId, cancellationToken);
        if (conversation == null)
            return false;

        conversation.IsArchived = true;
        conversation.Status = ConversationStatus.Archived;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AddFeedbackAsync(
        Guid messageId,
        Guid userId,
        MessageFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify message exists and user has access
        var message = await _db.NoticeMessages
            .Include(m => m.Conversation)
            .Where(m => m.Id == messageId)
            .Where(m => m.Conversation.OrganizationId == _tenantContext.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (message == null)
            throw new KeyNotFoundException("Message not found");

        // Check for existing feedback
        var existingFeedback = await _db.Set<MessageFeedback>()
            .FirstOrDefaultAsync(f => f.MessageId == messageId, cancellationToken);

        if (existingFeedback != null)
        {
            existingFeedback.Rating = request.Rating;
            existingFeedback.FeedbackText = request.FeedbackText;
            existingFeedback.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var feedback = new MessageFeedback
            {
                MessageId = messageId,
                UserId = userId,
                Rating = request.Rating,
                FeedbackText = request.FeedbackText
            };
            _db.Set<MessageFeedback>().Add(feedback);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConversationContext> GetConversationContextAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _db.NoticeConversations
            .Where(c => c.Id == conversationId)
            .Where(c => c.OrganizationId == _tenantContext.OrganizationId)
            .Include(c => c.Notice)
                .ThenInclude(n => n.AiReport)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation == null)
            throw new KeyNotFoundException("Conversation not found");

        var messages = await _db.NoticeMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        // Get the latest summary if any
        var summary = await _db.Set<ConversationSummary>()
            .Where(s => s.ConversationId == conversationId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        return new ConversationContext(
            conversation.Notice,
            conversation.Notice.AiReport,
            conversation,
            messages,
            summary
        );
    }

    private async Task<NoticeConversation?> GetConversationEntityAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _db.NoticeConversations
            .Where(c => c.Id == conversationId)
            .Where(c => c.OrganizationId == _tenantContext.OrganizationId)
            .Where(c => c.UserId == userId || HasOrgAdminAccess(userId))
            .Where(c => c.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private bool HasOrgAdminAccess(Guid userId)
    {
        // TODO: Implement proper role checking
        return false;
    }

    private static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private string? RenderMarkdown(string content)
    {
        try
        {
            return Markdown.ToHtml(content, _markdownPipeline);
        }
        catch
        {
            return null;
        }
    }

    private static ConversationDto MapToDto(NoticeConversation c, NoticeMessage? lastMessage)
    {
        return new ConversationDto(
            c.Id,
            c.NoticeId,
            c.Title,
            c.Status,
            c.MessageCount,
            c.LastMessageAt,
            c.CreatedAt,
            lastMessage != null ? MapToMessageDto(lastMessage) : null
        );
    }

    private static ConversationDetailDto MapToDetailDto(
        NoticeConversation c,
        List<NoticeMessage> messages,
        bool hasMore = false,
        string? nextCursor = null)
    {
        return new ConversationDetailDto(
            c.Id,
            c.NoticeId,
            c.Title,
            c.Status,
            c.MessageCount,
            c.TotalTokens,
            c.CreatedAt,
            c.LastMessageAt,
            messages.Select(MapToMessageDto).ToList(),
            hasMore,
            nextCursor
        );
    }

    private static MessageDto MapToMessageDto(NoticeMessage m)
    {
        return new MessageDto(
            m.Id,
            m.Role,
            m.Content,
            m.ContentHtml,
            m.Citations?.Select(c => new CitationDto(c.Source, c.Reference, c.Quote)).ToList(),
            m.CreatedAt,
            m.TokenCount,
            m.ModelId,
            m.IsError,
            m.Feedbacks.FirstOrDefault() is { } feedback ? new FeedbackDto(feedback.Rating, feedback.FeedbackText) : null
        );
    }
}
