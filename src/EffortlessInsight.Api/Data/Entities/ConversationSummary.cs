using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Stores AI-generated summaries of conversation history to manage context window limits.
/// Used when conversations exceed the token limit to preserve context.
/// </summary>
public class ConversationSummary : BaseEntity
{
    [Required]
    public Guid ConversationId { get; set; }
    public NoticeConversation Conversation { get; set; } = null!;

    /// <summary>
    /// The AI-generated summary of the conversation up to this point.
    /// </summary>
    [Required]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Number of messages covered by this summary.
    /// </summary>
    public int CoveredMessageCount { get; set; }

    /// <summary>
    /// The ID of the last message included in this summary.
    /// </summary>
    [Required]
    public Guid LastMessageId { get; set; }
    public NoticeMessage LastMessage { get; set; } = null!;

    /// <summary>
    /// Token count of the summary itself.
    /// </summary>
    public int TokenCount { get; set; }
}
