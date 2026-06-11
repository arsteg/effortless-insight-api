using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Aggregated SLA metrics for workflow performance tracking.
/// </summary>
public class WorkflowSlaMetric : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid WorkflowTemplateId { get; set; }
    public WorkflowTemplate WorkflowTemplate { get; set; } = null!;

    /// <summary>
    /// Stage key for stage-specific metrics. Null for overall workflow metrics.
    /// </summary>
    [MaxLength(50)]
    public string? StageKey { get; set; }

    /// <summary>
    /// Period type: daily, weekly, monthly
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PeriodType { get; set; } = MetricPeriodTypes.Daily;

    /// <summary>
    /// Start of the metric period.
    /// </summary>
    [Required]
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// End of the metric period.
    /// </summary>
    [Required]
    public DateTime PeriodEnd { get; set; }

    // Volume metrics
    public int TotalNotices { get; set; }
    public int NoticesEntered { get; set; }
    public int NoticesCompleted { get; set; }
    public int NoticesInProgress { get; set; }

    // SLA compliance metrics
    public int SlaMetCount { get; set; }
    public int SlaBreachedCount { get; set; }
    public int SlaWarningCount { get; set; }

    /// <summary>
    /// SLA compliance rate (0-100).
    /// </summary>
    public decimal SlaComplianceRate { get; set; }

    // Time metrics (in minutes)
    public int TotalProcessingTimeMinutes { get; set; }
    public int AverageProcessingTimeMinutes { get; set; }
    public int MedianProcessingTimeMinutes { get; set; }
    public int MinProcessingTimeMinutes { get; set; }
    public int MaxProcessingTimeMinutes { get; set; }

    // Escalation metrics
    public int EscalationCount { get; set; }
    public int ReassignmentCount { get; set; }

    // Team metrics
    public int UniqueAssignees { get; set; }
    public decimal AverageNoticesPerAssignee { get; set; }

    /// <summary>
    /// Breakdown by assignee (for detailed reporting).
    /// </summary>
    public Dictionary<string, AssigneeMetrics>? AssigneeBreakdown { get; set; }

    /// <summary>
    /// Breakdown by notice type.
    /// </summary>
    public Dictionary<string, int>? NoticeTypeBreakdown { get; set; }

    /// <summary>
    /// Breakdown by priority.
    /// </summary>
    public Dictionary<string, int>? PriorityBreakdown { get; set; }

    /// <summary>
    /// When this metric was last calculated.
    /// </summary>
    public DateTime CalculatedAt { get; set; }
}

/// <summary>
/// Metrics for individual assignees.
/// </summary>
public class AssigneeMetrics
{
    public int NoticesAssigned { get; set; }
    public int NoticesCompleted { get; set; }
    public int SlaMetCount { get; set; }
    public int SlaBreachedCount { get; set; }
    public int AverageTimeMinutes { get; set; }
}

/// <summary>
/// Metric period type constants.
/// </summary>
public static class MetricPeriodTypes
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";

    public static readonly string[] All = [Daily, Weekly, Monthly, Quarterly, Yearly];

    public static bool IsValid(string type) => All.Contains(type, StringComparer.OrdinalIgnoreCase);
}
