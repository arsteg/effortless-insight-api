using System.Text;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.AIChat.Providers;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Builds prompts for AI chat with GST notice context.
/// </summary>
public class PromptBuilderService : IPromptBuilderService
{
    public string PromptVersion => "1.0.0";

    private const string SystemPromptTemplate = """
        You are an expert GST (Goods and Services Tax) consultant assistant for Indian businesses.
        You help users understand and respond to GST notices they have received.

        ## Your Knowledge Sources
        You MUST base ALL your answers on the following sources ONLY:
        1. The GST Notice document provided below
        2. The AI analysis of this notice
        3. Previous conversation history

        ## Critical Rules
        - NEVER fabricate or invent information not present in the sources
        - NEVER make up legal citations, case laws, or sections not mentioned in the notice
        - If information is not available in the sources, clearly state: "This information is not available in the notice or analysis."
        - Always cite your source when providing information (e.g., "According to the notice...", "The analysis indicates...")
        - If legal interpretation is required, recommend consulting a qualified GST practitioner
        - Explain complex GST concepts in simple terms when asked
        - Provide practical, actionable advice based on the notice content

        ## Response Guidelines
        - Be concise but thorough
        - Use bullet points for lists and action items
        - Format amounts in Indian currency format (e.g., ₹1,23,456)
        - When discussing deadlines, clearly state the date and remaining time
        - If uncertain about something, acknowledge the uncertainty
        - Suggest practical next steps when appropriate

        ## Notice Information
        {NOTICE_CONTEXT}

        ## AI Analysis Summary
        {ANALYSIS_CONTEXT}

        ## Current Conversation
        The user is asking questions about this specific notice. Maintain context from previous messages in this conversation.
        """;

    public string BuildSystemPrompt(Notice notice, NoticeAiReport? aiReport)
    {
        var noticeContext = BuildNoticeContext(notice);
        var analysisContext = BuildAnalysisContext(aiReport);

        return SystemPromptTemplate
            .Replace("{NOTICE_CONTEXT}", noticeContext)
            .Replace("{ANALYSIS_CONTEXT}", analysisContext);
    }

    private static string BuildNoticeContext(Notice notice)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Notice Number: {notice.NoticeNumber ?? "Not specified"}");
        sb.AppendLine($"Notice Type: {notice.NoticeType ?? "Unknown"}");
        sb.AppendLine($"Notice Category: {notice.NoticeCategory ?? "Unknown"}");
        sb.AppendLine($"GSTIN: {notice.Gstin ?? "Not specified"}");
        sb.AppendLine($"Issue Date: {notice.IssueDate?.ToString("dd-MMM-yyyy") ?? "Not specified"}");
        sb.AppendLine($"Response Deadline: {notice.ResponseDeadline?.ToString("dd-MMM-yyyy") ?? "Not specified"}");

        if (notice.TaxAmount.HasValue)
            sb.AppendLine($"Tax Amount: ₹{notice.TaxAmount:N2}");
        if (notice.PenaltyAmount.HasValue)
            sb.AppendLine($"Penalty Amount: ₹{notice.PenaltyAmount:N2}");
        if (notice.InterestAmount.HasValue)
            sb.AppendLine($"Interest Amount: ₹{notice.InterestAmount:N2}");
        if (notice.TotalDemand.HasValue)
            sb.AppendLine($"Total Demand: ₹{notice.TotalDemand:N2}");

        sb.AppendLine($"Issuing Authority: {notice.IssuingAuthority ?? "Not specified"}");
        sb.AppendLine($"Section: {notice.Section ?? "Not specified"}");

        if (notice.PeriodFrom.HasValue || notice.PeriodTo.HasValue)
        {
            sb.AppendLine($"Period: {notice.PeriodFrom?.ToString("MMM-yyyy") ?? "N/A"} to {notice.PeriodTo?.ToString("MMM-yyyy") ?? "N/A"}");
        }

        if (!string.IsNullOrEmpty(notice.OcrText))
        {
            sb.AppendLine();
            sb.AppendLine("--- FULL NOTICE TEXT (OCR) ---");
            sb.AppendLine(TruncateText(notice.OcrText, 8000)); // Limit OCR text
        }

        return sb.ToString();
    }

    private static string BuildAnalysisContext(NoticeAiReport? report)
    {
        if (report == null)
            return "AI analysis not available.";

        var sb = new StringBuilder();

        if (report.RiskScore.HasValue)
        {
            sb.AppendLine($"Risk Level: {report.RiskLevel ?? "Unknown"} (Score: {report.RiskScore}/100)");
        }

        if (!string.IsNullOrEmpty(report.SummaryEn))
        {
            sb.AppendLine();
            sb.AppendLine("Summary (English):");
            sb.AppendLine(report.SummaryEn);
        }

        if (!string.IsNullOrEmpty(report.PlainEnglish))
        {
            sb.AppendLine();
            sb.AppendLine("Plain English Explanation:");
            sb.AppendLine(report.PlainEnglish);
        }

        if (report.ActionItems != null && report.ActionItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recommended Actions:");
            foreach (var item in report.ActionItems)
            {
                sb.AppendLine($"- {item.Key}: {item.Value}");
            }
        }

        if (report.RequiredDocuments != null && report.RequiredDocuments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Required Documents:");
            foreach (var doc in report.RequiredDocuments)
            {
                sb.AppendLine($"- {doc.Key}: {doc.Value}");
            }
        }

        if (report.LegalReferences != null && report.LegalReferences.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Legal References:");
            foreach (var legal in report.LegalReferences)
            {
                sb.AppendLine($"- {legal.Key}: {legal.Value}");
            }
        }

        return sb.Length > 0 ? sb.ToString() : "AI analysis not available.";
    }

    public List<AIMessage> BuildConversationContext(
        Notice notice,
        NoticeAiReport? aiReport,
        List<NoticeMessage> messages,
        string? summary,
        int maxTokens)
    {
        var result = new List<AIMessage>();
        var currentTokens = 0;

        // Include summary if available
        if (!string.IsNullOrEmpty(summary))
        {
            result.Add(new AIMessage("system", $"Previous conversation summary:\n{summary}"));
            currentTokens += EstimateTokens(summary);
        }

        // Add messages from oldest to newest, respecting token limit
        var messagesToInclude = new List<NoticeMessage>();

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var tokens = msg.TokenCount ?? EstimateTokens(msg.Content);

            if (currentTokens + tokens > maxTokens)
                break;

            messagesToInclude.Insert(0, msg);
            currentTokens += tokens;
        }

        foreach (var msg in messagesToInclude)
        {
            result.Add(new AIMessage(msg.Role, msg.Content));
        }

        return result;
    }

    public List<string> GenerateSuggestedQuestions(Notice notice, NoticeAiReport? aiReport)
    {
        var questions = new List<string>
        {
            "Explain this notice in simple terms",
            "What should I do first?",
            "What is the deadline for responding?"
        };

        if (notice.TotalDemand.HasValue && notice.TotalDemand > 0)
        {
            questions.Add($"Explain the tax demand of ₹{notice.TotalDemand:N0}");
        }

        if (aiReport?.ActionItems != null && aiReport.ActionItems.Count > 0)
        {
            questions.Add("What documents do I need to prepare?");
        }

        if (!string.IsNullOrEmpty(notice.Section))
        {
            questions.Add($"Explain Section {notice.Section}");
        }

        questions.AddRange([
            "Can I appeal this notice?",
            "What penalties might apply?",
            "Summarize in Hindi"
        ]);

        return questions.Take(8).ToList();
    }

    public string GenerateConversationTitle(string firstMessage)
    {
        // Generate a short title from the first message
        var cleaned = firstMessage
            .Replace("\n", " ")
            .Replace("\r", "")
            .Trim();

        if (cleaned.Length > 50)
        {
            // Try to cut at a word boundary
            var truncated = cleaned[..47];
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 30)
            {
                truncated = truncated[..lastSpace];
            }
            return truncated + "...";
        }

        return cleaned;
    }

    private static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "\n...[truncated]";
    }
}
