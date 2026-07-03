using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Audit log for AI API calls, tracking usage, costs, and errors.
/// </summary>
public class AIAuditLog : BaseEntity
{
    public Guid? ConversationId { get; set; }
    public NoticeConversation? Conversation { get; set; }

    public Guid? MessageId { get; set; }
    public NoticeMessage? Message { get; set; }

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// The AI model used (e.g., "gpt-4o", "claude-3-sonnet").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Version of the prompt template used.
    /// </summary>
    [MaxLength(20)]
    public string? PromptVersion { get; set; }

    /// <summary>
    /// Number of input tokens sent to the AI.
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// Number of output tokens received from the AI.
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// Total tokens (input + output).
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Estimated cost in USD for this API call.
    /// </summary>
    [Column(TypeName = "decimal(10,6)")]
    public decimal? EstimatedCost { get; set; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; set; }

    /// <summary>
    /// Status of the API call: success, error, timeout, rate_limited.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = AIAuditStatus.Success;

    /// <summary>
    /// Error code if the call failed.
    /// </summary>
    [MaxLength(50)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Error message if the call failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// AI audit log status constants.
/// </summary>
public static class AIAuditStatus
{
    public const string Success = "success";
    public const string Error = "error";
    public const string Timeout = "timeout";
    public const string RateLimited = "rate_limited";

    public static readonly string[] All = [Success, Error, Timeout, RateLimited];
}
