using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks deadlines for notices, including extracted and manually set deadlines.
/// </summary>
public class NoticeDeadline : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    /// <summary>
    /// Type of deadline: response, payment, hearing, appeal, compliance, custom
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string DeadlineType { get; set; } = string.Empty;

    /// <summary>
    /// Original deadline date (before any extensions).
    /// </summary>
    [Required]
    public DateTime OriginalDeadline { get; set; }

    /// <summary>
    /// Current effective deadline (after extensions).
    /// </summary>
    [Required]
    public DateTime EffectiveDeadline { get; set; }

    /// <summary>
    /// How the deadline was determined.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Source { get; set; } = DeadlineSources.Manual;

    /// <summary>
    /// Confidence score for AI-extracted deadlines (0-100).
    /// </summary>
    public int? ExtractionConfidence { get; set; }

    /// <summary>
    /// Original text from which deadline was extracted.
    /// </summary>
    [MaxLength(500)]
    public string? ExtractedText { get; set; }

    /// <summary>
    /// Whether the deadline has been verified by a user.
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// User who verified the deadline.
    /// </summary>
    public Guid? VerifiedById { get; set; }
    public ApplicationUser? VerifiedBy { get; set; }

    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Current status of this deadline.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = DeadlineStatuses.Pending;

    /// <summary>
    /// Priority level for this deadline.
    /// </summary>
    [MaxLength(20)]
    public string Priority { get; set; } = DeadlinePriorities.Medium;

    /// <summary>
    /// Whether reminder notifications are enabled.
    /// </summary>
    public bool ReminderEnabled { get; set; } = true;

    /// <summary>
    /// Days before deadline to send reminders.
    /// </summary>
    public List<int> ReminderDaysBefore { get; set; } = [7, 3, 1];

    /// <summary>
    /// Last reminder sent date.
    /// </summary>
    public DateTime? LastReminderSentAt { get; set; }

    /// <summary>
    /// Notes about this deadline.
    /// </summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    // Navigation properties
    public ICollection<DeadlineExtension> Extensions { get; set; } = [];
}

/// <summary>
/// Deadline type constants.
/// </summary>
public static class DeadlineTypes
{
    public const string Response = "response";
    public const string Payment = "payment";
    public const string Hearing = "hearing";
    public const string Appeal = "appeal";
    public const string Compliance = "compliance";
    public const string Custom = "custom";

    public static readonly string[] All = [Response, Payment, Hearing, Appeal, Compliance, Custom];

    public static bool IsValid(string type) => All.Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Deadline source constants.
/// </summary>
public static class DeadlineSources
{
    public const string Manual = "manual";
    public const string AiExtracted = "ai_extracted";
    public const string RegexExtracted = "regex_extracted";
    public const string Calculated = "calculated";
    public const string Imported = "imported";

    public static readonly string[] All = [Manual, AiExtracted, RegexExtracted, Calculated, Imported];

    public static bool IsValid(string source) => All.Contains(source, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Deadline status constants.
/// </summary>
public static class DeadlineStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Extended = "extended";
    public const string Missed = "missed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = [Pending, InProgress, Completed, Extended, Missed, Cancelled];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Deadline priority constants.
/// </summary>
public static class DeadlinePriorities
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static readonly string[] All = [Critical, High, Medium, Low];

    public static bool IsValid(string priority) => All.Contains(priority, StringComparer.OrdinalIgnoreCase);
}
