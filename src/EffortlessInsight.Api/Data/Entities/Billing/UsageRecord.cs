using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Tracks resource usage per billing period.
/// </summary>
public class UsageRecord : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Start of the usage period.
    /// </summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>
    /// End of the usage period.
    /// </summary>
    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// Number of notices created in this period.
    /// </summary>
    public int NoticesCount { get; set; }

    /// <summary>
    /// Number of active users in this period.
    /// </summary>
    public int UsersCount { get; set; }

    /// <summary>
    /// Storage used in bytes.
    /// </summary>
    public long StorageBytes { get; set; }

    /// <summary>
    /// Number of API calls made.
    /// </summary>
    public int ApiCalls { get; set; }

    /// <summary>
    /// Peak concurrent users.
    /// </summary>
    public int PeakConcurrentUsers { get; set; }

    /// <summary>
    /// Number of AI analyses performed.
    /// </summary>
    public int AiAnalysesCount { get; set; }

    /// <summary>
    /// Number of documents processed.
    /// </summary>
    public int DocumentsProcessed { get; set; }

    /// <summary>
    /// Number of emails sent.
    /// </summary>
    public int EmailsSent { get; set; }

    /// <summary>
    /// Number of SMS sent.
    /// </summary>
    public int SmsSent { get; set; }

    /// <summary>
    /// Last time usage was recalculated.
    /// </summary>
    public DateTime? LastCalculatedAt { get; set; }

    /// <summary>
    /// Additional usage metrics.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetrics { get; set; }
}
