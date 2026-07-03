using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Retrieves and manages context for AI chat conversations.
/// </summary>
public class ContextRetrievalService : IContextRetrievalService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly AIChatOptions _options;
    private readonly ILogger<ContextRetrievalService> _logger;

    // Approximate token costs for different context components
    private const int SYSTEM_PROMPT_BASE_TOKENS = 500;
    private const int NOTICE_METADATA_TOKENS = 200;
    private const int SAFETY_MARGIN_TOKENS = 500;

    public ContextRetrievalService(
        ApplicationDbContext db,
        ITenantContext tenantContext,
        IOptions<AIChatOptions> options,
        ILogger<ContextRetrievalService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatContext> GetContextAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        // Load conversation with notice and AI report
        var conversation = await _db.NoticeConversations
            .Where(c => c.Id == conversationId)
            .Where(c => c.OrganizationId == _tenantContext.OrganizationId)
            .Include(c => c.Notice)
                .ThenInclude(n => n.AiReport)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation == null)
            throw new KeyNotFoundException($"Conversation {conversationId} not found");

        // Load messages ordered by creation time
        var messages = await _db.NoticeMessages
            .Where(m => m.ConversationId == conversationId)
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        // Get the latest summary if available
        var summary = await _db.ConversationSummaries
            .Where(s => s.ConversationId == conversationId)
            .Where(s => s.DeletedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        // Calculate token estimates
        var estimatedTokens = EstimateContextTokensInternal(
            conversation.Notice,
            conversation.Notice.AiReport,
            messages,
            summary);

        return new ChatContext(
            conversation.Notice,
            conversation.Notice.AiReport,
            conversation,
            messages,
            summary,
            estimatedTokens
        );
    }

    public async Task<NoticeContext> GetNoticeContextAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .Where(n => n.Id == noticeId)
            .Where(n => n.OrganizationId == _tenantContext.OrganizationId)
            .Include(n => n.AiReport)
            .FirstOrDefaultAsync(cancellationToken);

        if (notice == null)
            throw new KeyNotFoundException($"Notice {noticeId} not found");

        var noticeTokens = EstimateNoticeTokens(notice);
        var reportTokens = EstimateReportTokens(notice.AiReport);

        return new NoticeContext(notice, notice.AiReport, noticeTokens, reportTokens);
    }

    public int EstimateContextTokens(ChatContext context)
    {
        return context.EstimatedTokens;
    }

    public bool NeedsSummarization(ChatContext context, int maxTokens)
    {
        // Check if we're approaching the token limit
        var threshold = maxTokens - SAFETY_MARGIN_TOKENS;
        return context.EstimatedTokens > threshold;
    }

    private int EstimateContextTokensInternal(
        Notice notice,
        NoticeAiReport? aiReport,
        List<NoticeMessage> messages,
        string? summary)
    {
        var total = SYSTEM_PROMPT_BASE_TOKENS;

        // Notice context
        total += EstimateNoticeTokens(notice);

        // AI report context
        total += EstimateReportTokens(aiReport);

        // Summary if present
        if (!string.IsNullOrEmpty(summary))
        {
            total += EstimateTokens(summary);
        }

        // Messages
        foreach (var message in messages)
        {
            total += message.TokenCount ?? EstimateTokens(message.Content);
        }

        return total;
    }

    private int EstimateNoticeTokens(Notice notice)
    {
        var tokens = NOTICE_METADATA_TOKENS;

        // OCR text is typically the largest component
        if (!string.IsNullOrEmpty(notice.OcrText))
        {
            // We typically truncate OCR text to ~8000 chars in the prompt
            var ocrLength = Math.Min(notice.OcrText.Length, 8000);
            tokens += EstimateTokens(notice.OcrText[..ocrLength]);
        }

        return tokens;
    }

    private int EstimateReportTokens(NoticeAiReport? report)
    {
        if (report == null)
            return 50; // "AI analysis not available"

        var tokens = 0;

        if (!string.IsNullOrEmpty(report.SummaryEn))
            tokens += EstimateTokens(report.SummaryEn);

        if (!string.IsNullOrEmpty(report.PlainEnglish))
            tokens += EstimateTokens(report.PlainEnglish);

        // Action items, documents, legal references
        if (report.ActionItems != null)
            tokens += report.ActionItems.Count * 30;

        if (report.RequiredDocuments != null)
            tokens += report.RequiredDocuments.Count * 20;

        if (report.LegalReferences != null)
            tokens += report.LegalReferences.Count * 40;

        return Math.Max(tokens, 100);
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // Rough estimate: ~4 chars per token for English
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}
