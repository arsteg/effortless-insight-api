using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a single message in a notice conversation (either user or assistant).
/// </summary>
public class NoticeMessage : BaseEntity
{
    [Required]
    public Guid ConversationId { get; set; }
    public NoticeConversation Conversation { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = MessageRole.User;

    /// <summary>
    /// The raw text content of the message (Markdown for assistant messages).
    /// </summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// HTML-rendered version of the content (for assistant messages with Markdown).
    /// </summary>
    public string? ContentHtml { get; set; }

    /// <summary>
    /// Number of tokens used by this message.
    /// </summary>
    public int? TokenCount { get; set; }

    /// <summary>
    /// The AI model used to generate this response (for assistant messages).
    /// </summary>
    [MaxLength(100)]
    public string? ModelId { get; set; }

    /// <summary>
    /// Version of the prompt template used to generate this response.
    /// </summary>
    [MaxLength(20)]
    public string? PromptVersion { get; set; }

    /// <summary>
    /// Time taken to generate the response in milliseconds.
    /// </summary>
    public int? ResponseTimeMs { get; set; }

    /// <summary>
    /// Citations/references to source material (notice, analysis, etc.).
    /// Stored as JSONB.
    /// </summary>
    public List<Citation>? Citations { get; set; }

    /// <summary>
    /// Whether this message represents an error response.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Error message if IsError is true.
    /// </summary>
    public string? ErrorMessage { get; set; }

    // Navigation
    public ICollection<MessageFeedback> Feedbacks { get; set; } = new List<MessageFeedback>();
}

/// <summary>
/// Message role constants.
/// </summary>
public static class MessageRole
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string System = "system";

    public static readonly string[] All = [User, Assistant, System];

    public static bool IsValid(string role) => All.Contains(role);
}

/// <summary>
/// Represents a citation to source material in an AI response.
/// </summary>
public record Citation(
    string Source,      // "notice", "analysis", "conversation"
    string Reference,   // Section or message ID
    string? Quote       // Exact quote if applicable
);
