using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents user feedback on an AI-generated message (thumbs up/down).
/// </summary>
public class MessageFeedback : BaseEntity
{
    [Required]
    public Guid MessageId { get; set; }
    public NoticeMessage Message { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Rating: 1 for positive (thumbs up), -1 for negative (thumbs down).
    /// </summary>
    [Required]
    public int Rating { get; set; }

    /// <summary>
    /// Optional text feedback from the user explaining their rating.
    /// </summary>
    [MaxLength(2000)]
    public string? FeedbackText { get; set; }
}
