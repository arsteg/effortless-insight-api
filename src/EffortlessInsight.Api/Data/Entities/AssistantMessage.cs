using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// One message in an app-wide assistant conversation. Tenant scoping flows
/// through the parent <see cref="AssistantConversation"/>.
/// </summary>
public class AssistantMessage : BaseEntity
{
    [Required]
    public Guid ConversationId { get; set; }
    public AssistantConversation Conversation { get; set; } = null!;

    /// <summary>"user" or "assistant".</summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>Knowledge references used for the answer (JSON array of strings).</summary>
    public List<string> Citations { get; set; } = [];

    /// <summary>
    /// Actions attached to the message (navigate buttons / confirmation cards),
    /// stored as the raw JSON array emitted by the AI service.
    /// </summary>
    public string? ActionsJson { get; set; }

    /// <summary>
    /// Audit of read-tool calls made during this turn (tool name + status only;
    /// never parameters containing user data, never tokens). Raw JSON array.
    /// </summary>
    public string? ToolCallsJson { get; set; }

    public int TokenCount { get; set; }

    [MaxLength(100)]
    public string? ModelId { get; set; }

    public int? ResponseTimeMs { get; set; }

    public bool IsError { get; set; }
}

/// <summary>Role constants for assistant messages.</summary>
public static class AssistantMessageRole
{
    public const string User = "user";
    public const string Assistant = "assistant";
}
