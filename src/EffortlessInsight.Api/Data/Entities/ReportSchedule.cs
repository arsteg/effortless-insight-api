using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// GAP-RPT-006: Scheduled Reports - Schedule for automatic report generation and delivery
/// </summary>
public class ReportSchedule : BaseEntity
{
    [Required]
    public Guid SavedReportId { get; set; }
    public SavedReport SavedReport { get; set; } = null!;

    /// <summary>
    /// Frequency of report generation: daily, weekly, monthly
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Frequency { get; set; } = ScheduleFrequency.Weekly;

    /// <summary>
    /// Day of week for weekly schedules (0=Sunday, 1=Monday, ..., 6=Saturday)
    /// </summary>
    public int? DayOfWeek { get; set; }

    /// <summary>
    /// Day of month for monthly schedules (1-31)
    /// </summary>
    public int? DayOfMonth { get; set; }

    /// <summary>
    /// Time of day to generate the report
    /// </summary>
    public TimeOnly TimeOfDay { get; set; } = new TimeOnly(9, 0); // Default 9 AM

    /// <summary>
    /// Timezone for scheduling (IANA timezone identifier)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    /// <summary>
    /// List of email addresses to send the report to (stored as JSONB)
    /// </summary>
    public List<string> Recipients { get; set; } = [];

    /// <summary>
    /// Export format for the report: pdf, excel, csv
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string ExportFormat { get; set; } = ExportFormats.Pdf;

    /// <summary>
    /// Whether the schedule is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last time the scheduled report was run
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Next scheduled run time (calculated based on frequency and timezone)
    /// </summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// Status of the last run: success, failed, pending
    /// </summary>
    [MaxLength(20)]
    public string? LastRunStatus { get; set; }

    /// <summary>
    /// Error message if the last run failed
    /// </summary>
    [MaxLength(1000)]
    public string? LastRunError { get; set; }

    /// <summary>
    /// Number of consecutive failures (used for backoff/alerting)
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// User who created the schedule
    /// </summary>
    [Required]
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;
}

/// <summary>
/// Schedule frequency options
/// </summary>
public static class ScheduleFrequency
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly string[] All = [Daily, Weekly, Monthly];

    public static bool IsValid(string frequency) =>
        All.Contains(frequency, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Export format options
/// </summary>
public static class ExportFormats
{
    public const string Pdf = "pdf";
    public const string Excel = "excel";
    public const string Csv = "csv";

    public static readonly string[] All = [Pdf, Excel, Csv];

    public static bool IsValid(string format) =>
        All.Contains(format, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Schedule run status
/// </summary>
public static class ScheduleRunStatus
{
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Pending = "pending";
    public const string Cancelled = "cancelled";
}
