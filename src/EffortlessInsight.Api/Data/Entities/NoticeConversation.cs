using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a conversation between a user and the AI assistant about a specific notice.
/// </summary>
public class NoticeConversation : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [MaxLength(255)]
    public string Title { get; set; } = "New Conversation";

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = ConversationStatus.Active;

    /// <summary>
    /// Total number of messages in this conversation.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Total tokens consumed across all messages.
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Timestamp of the last message in this conversation.
    /// </summary>
    public DateTime? LastMessageAt { get; set; }

    /// <summary>
    /// Whether this conversation has been archived by the user.
    /// </summary>
    public bool IsArchived { get; set; }

    // Navigation properties
    public ICollection<NoticeMessage> Messages { get; set; } = [];
    public ICollection<ConversationSummary> Summaries { get; set; } = [];
}

/// <summary>
/// Conversation status constants.
/// </summary>
public static class ConversationStatus
{
    public const string Active = "active";
    public const string Archived = "archived";

    public static readonly string[] All = [Active, Archived];

    public static bool IsValid(string status) => All.Contains(status);
}
