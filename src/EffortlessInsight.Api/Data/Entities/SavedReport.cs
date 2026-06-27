using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// GAP-RPT-003: Custom Report Builder - A saved report configuration
/// </summary>
public class SavedReport : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Type of report: notices, tasks, users, compliance
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ReportType { get; set; } = ReportTypes.Notices;

    /// <summary>
    /// Report configuration stored as JSONB (columns, filters, grouping, etc.)
    /// </summary>
    public ReportConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Whether this report is shared with all organization members
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Last time this report was executed
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Number of times this report has been run
    /// </summary>
    public int RunCount { get; set; }

    // Navigation properties
    public ICollection<ReportSchedule> Schedules { get; set; } = [];
}

/// <summary>
/// Report configuration JSONB model
/// </summary>
public class ReportConfiguration
{
    /// <summary>
    /// Columns to include in the report output
    /// </summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>
    /// Filters to apply to the report data
    /// </summary>
    public List<ReportFilter> Filters { get; set; } = [];

    /// <summary>
    /// Field to group results by
    /// </summary>
    public string? GroupBy { get; set; }

    /// <summary>
    /// Field to sort results by
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Sort in descending order
    /// </summary>
    public bool SortDescending { get; set; }

    /// <summary>
    /// Date range configuration for filtering
    /// </summary>
    public DateRangeConfig? DateRange { get; set; }

    /// <summary>
    /// Optional limit on number of results
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Include summary statistics
    /// </summary>
    public bool IncludeSummary { get; set; } = true;
}

/// <summary>
/// Report filter configuration
/// </summary>
public class ReportFilter
{
    /// <summary>
    /// Field name to filter on
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Filter operator: eq, ne, gt, lt, gte, lte, contains, in, between
    /// </summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>
    /// Filter value (can be string, number, date, or array)
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// Date range configuration for reports
/// </summary>
public class DateRangeConfig
{
    /// <summary>
    /// Preset range: today, yesterday, this_week, last_week, this_month,
    /// last_month, this_quarter, last_quarter, this_year, last_year, custom
    /// </summary>
    public string? Preset { get; set; }

    /// <summary>
    /// Custom start date (used when Preset is "custom")
    /// </summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>
    /// Custom end date (used when Preset is "custom")
    /// </summary>
    public DateOnly? EndDate { get; set; }
}

/// <summary>
/// Available report types
/// </summary>
public static class ReportTypes
{
    public const string Notices = "notices";
    public const string Tasks = "tasks";
    public const string Users = "users";
    public const string Compliance = "compliance";

    public static readonly string[] All = [Notices, Tasks, Users, Compliance];

    public static bool IsValid(string type) =>
        All.Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Available filter operators
/// </summary>
public static class FilterOperators
{
    public const string Equal = "eq";
    public const string NotEqual = "ne";
    public const string GreaterThan = "gt";
    public const string LessThan = "lt";
    public const string GreaterThanOrEqual = "gte";
    public const string LessThanOrEqual = "lte";
    public const string Contains = "contains";
    public const string In = "in";
    public const string Between = "between";
    public const string IsNull = "is_null";
    public const string IsNotNull = "is_not_null";

    public static readonly string[] All =
    [
        Equal, NotEqual, GreaterThan, LessThan, GreaterThanOrEqual,
        LessThanOrEqual, Contains, In, Between, IsNull, IsNotNull
    ];

    public static bool IsValid(string op) =>
        All.Contains(op, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Available date range presets
/// </summary>
public static class DateRangePresets
{
    public const string Today = "today";
    public const string Yesterday = "yesterday";
    public const string ThisWeek = "this_week";
    public const string LastWeek = "last_week";
    public const string ThisMonth = "this_month";
    public const string LastMonth = "last_month";
    public const string ThisQuarter = "this_quarter";
    public const string LastQuarter = "last_quarter";
    public const string ThisYear = "this_year";
    public const string LastYear = "last_year";
    public const string Last7Days = "last_7_days";
    public const string Last30Days = "last_30_days";
    public const string Last90Days = "last_90_days";
    public const string Custom = "custom";

    public static readonly string[] All =
    [
        Today, Yesterday, ThisWeek, LastWeek, ThisMonth, LastMonth,
        ThisQuarter, LastQuarter, ThisYear, LastYear, Last7Days,
        Last30Days, Last90Days, Custom
    ];

    public static bool IsValid(string preset) =>
        All.Contains(preset, StringComparer.OrdinalIgnoreCase);
}
