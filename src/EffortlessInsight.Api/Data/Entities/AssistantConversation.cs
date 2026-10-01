using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// An app-wide assistant conversation (not scoped to a single notice, unlike
/// <see cref="NoticeConversation"/>). Owned by one user within one organization;
/// the organization is the tenant boundary.
/// </summary>
public class AssistantConversation : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [MaxLength(255)]
    public string Title { get; set; } = "New Conversation";

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = ConversationStatus.Active;

    /// <summary>Platform the conversation was started from ("web" or "mobile").</summary>
    [MaxLength(10)]
    public string Platform { get; set; } = "web";

    public int MessageCount { get; set; }

    public int TotalTokens { get; set; }

    public DateTime? LastMessageAt { get; set; }

    // Navigation properties
    public ICollection<AssistantMessage> Messages { get; set; } = [];
}
