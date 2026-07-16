using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Analytics;

// =============================================================================
// INTERFACES
// =============================================================================

/// <summary>
/// Service for comparative analytics (GAP-RPT-009).
/// </summary>
public interface IComparativeAnalysisService
{
    /// <summary>
    /// Compares metrics between two time periods.
    /// </summary>
    Task<PeriodComparison> ComparePeriods(
        Guid orgId, DateRange current, DateRange previous, CancellationToken ct);
}

// =============================================================================
// DTOs
// =============================================================================

/// <summary>
/// Comparison results between two periods.
/// </summary>
public record PeriodComparison
{
    public required PeriodMetrics CurrentPeriod { get; init; }
    public required PeriodMetrics PreviousPeriod { get; init; }
    public required Dictionary<string, decimal> ChangePercent { get; init; }
    public required Dictionary<string, string> Trends { get; init; }
    public DateRange CurrentRange { get; init; } = null!;
    public DateRange PreviousRange { get; init; } = null!;
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Metrics for a single period.
/// </summary>
public record PeriodMetrics
{
    public int TotalNotices { get; init; }
    public int NewNotices { get; init; }
    public int ClosedNotices { get; init; }
    public int OverdueNotices { get; init; }
    public decimal TotalDemand { get; init; }
    public decimal AverageResolutionHours { get; init; }
    public decimal MedianResolutionHours { get; init; }
    public decimal SlaComplianceRate { get; init; }
    public int TasksCompleted { get; init; }
    public int CommentsAdded { get; init; }
    public Dictionary<string, int> ByStatus { get; init; } = [];
    public Dictionary<string, int> ByPriority { get; init; } = [];
    public Dictionary<string, int> ByCategory { get; init; } = [];
    public int UniqueAssignees { get; init; }
    public decimal AverageNoticesPerAssignee { get; init; }
}

// =============================================================================
// IMPLEMENTATION
// =============================================================================

public class ComparativeAnalysisService : IComparativeAnalysisService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ComparativeAnalysisService> _logger;

    // Thresholds for trend determination
    private const decimal SignificantChangeThreshold = 5m; // 5% change is significant

    public ComparativeAnalysisService(
        ApplicationDbContext context,
        ILogger<ComparativeAnalysisService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Helper method to convert DateOnly to UTC DateTime for PostgreSQL compatibility
    private static DateTime ToUtcDateTime(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);

    public async Task<PeriodComparison> ComparePeriods(
        Guid orgId, DateRange current, DateRange previous, CancellationToken ct)
    {
        _logger.LogInformation(
            "Comparing periods for org {OrgId}: Current {CurrentStart}-{CurrentEnd} vs Previous {PrevStart}-{PrevEnd}",
            orgId, current.StartDate, current.EndDate, previous.StartDate, previous.EndDate);

        // Calculate metrics for both periods in parallel
        var currentMetricsTask = CalculatePeriodMetricsAsync(orgId, current, ct);
        var previousMetricsTask = CalculatePeriodMetricsAsync(orgId, previous, ct);

        await Task.WhenAll(currentMetricsTask, previousMetricsTask);

        var currentMetrics = await currentMetricsTask;
        var previousMetrics = await previousMetricsTask;

        // Calculate change percentages
        var changePercent = CalculateChangePercent(currentMetrics, previousMetrics);

        // Determine trends
        var trends = DetermineTrends(changePercent);

        return new PeriodComparison
        {
            CurrentPeriod = currentMetrics,
            PreviousPeriod = previousMetrics,
            ChangePercent = changePercent,
            Trends = trends,
            CurrentRange = current,
            PreviousRange = previous
        };
    }

    private async Task<PeriodMetrics> CalculatePeriodMetricsAsync(
        Guid orgId, DateRange range, CancellationToken ct)
    {
        var startDateTime = ToUtcDateTime(range.StartDate, TimeOnly.MinValue);
        var endDateTime = ToUtcDateTime(range.EndDate, TimeOnly.MaxValue);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Get notices created in the period
        var notices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.CreatedAt >= startDateTime && n.CreatedAt <= endDateTime)
            .Select(n => new
            {
                n.Id,
                n.Status,
                n.Priority,
                Category = n.NoticeCategory ?? "uncategorized",
                n.ResponseDeadline,
                n.TotalDemand,
                n.AssignedToId,
                n.CreatedAt,
                n.UpdatedAt
            })
            .ToListAsync(ct);

        // Get closed notices in the period (may have been created earlier)
        var closedNotices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.Status == NoticeStatus.Closed)
            .Where(n => n.UpdatedAt >= startDateTime && n.UpdatedAt <= endDateTime)
            .Select(n => new
            {
                n.Id,
                n.CreatedAt,
                n.UpdatedAt,
                n.ResponseDeadline
            })
            .ToListAsync(ct);

        // Get tasks completed in the period
        var tasksCompleted = await _context.Tasks
            .Where(t => t.OrganizationId == orgId)
            .Where(t => t.Status == "completed")
            .Where(t => t.UpdatedAt >= startDateTime && t.UpdatedAt <= endDateTime)
            .CountAsync(ct);

        // Get comments added in the period
        var commentsAdded = await _context.Comments
            .Where(c => c.Notice!.OrganizationId == orgId)
            .Where(c => c.CreatedAt >= startDateTime && c.CreatedAt <= endDateTime)
            .CountAsync(ct);

        // Calculate resolution times
        var resolutionHours = closedNotices
            .Select(n => (decimal)((n.UpdatedAt - n.CreatedAt) ?? TimeSpan.Zero).TotalHours)
            .OrderBy(h => h)
            .ToList();

        var avgResolution = resolutionHours.Count > 0 ? resolutionHours.Average() : 0;
        var medianResolution = resolutionHours.Count > 0 ? GetMedian(resolutionHours) : 0;

        // Calculate SLA compliance
        var noticesWithDeadline = closedNotices.Where(n => n.ResponseDeadline.HasValue).ToList();
        var slaMet = noticesWithDeadline.Count(n =>
            n.UpdatedAt.HasValue && DateOnly.FromDateTime(n.UpdatedAt.Value) <= n.ResponseDeadline!.Value);
        var slaComplianceRate = noticesWithDeadline.Count > 0
            ? Math.Round((decimal)slaMet / noticesWithDeadline.Count * 100, 2)
            : 100m;

        // Calculate overdue
        var overdueCount = notices.Count(n =>
            n.ResponseDeadline.HasValue &&
            n.ResponseDeadline.Value < today &&
            n.Status != NoticeStatus.Closed &&
            n.Status != NoticeStatus.Archived);

        // Calculate assignee metrics
        var assignees = notices
            .Where(n => n.AssignedToId.HasValue)
            .Select(n => n.AssignedToId!.Value)
            .Distinct()
            .ToList();
        var uniqueAssignees = assignees.Count;
        var avgNoticesPerAssignee = uniqueAssignees > 0
            ? Math.Round((decimal)notices.Count / uniqueAssignees, 2)
            : 0;

        // Build breakdowns
        var byStatus = notices.GroupBy(n => n.Status)
            .ToDictionary(g => g.Key, g => g.Count());
        var byPriority = notices.GroupBy(n => n.Priority)
            .ToDictionary(g => g.Key, g => g.Count());
        var byCategory = notices.GroupBy(n => n.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new PeriodMetrics
        {
            TotalNotices = notices.Count,
            NewNotices = notices.Count,
            ClosedNotices = closedNotices.Count,
            OverdueNotices = overdueCount,
            TotalDemand = notices.Sum(n => n.TotalDemand ?? 0),
            AverageResolutionHours = Math.Round(avgResolution, 2),
            MedianResolutionHours = Math.Round(medianResolution, 2),
            SlaComplianceRate = slaComplianceRate,
            TasksCompleted = tasksCompleted,
            CommentsAdded = commentsAdded,
            ByStatus = byStatus,
            ByPriority = byPriority,
            ByCategory = byCategory,
            UniqueAssignees = uniqueAssignees,
            AverageNoticesPerAssignee = avgNoticesPerAssignee
        };
    }

    private static Dictionary<string, decimal> CalculateChangePercent(
        PeriodMetrics current, PeriodMetrics previous)
    {
        return new Dictionary<string, decimal>
        {
            ["totalNotices"] = CalculateChange(current.TotalNotices, previous.TotalNotices),
            ["newNotices"] = CalculateChange(current.NewNotices, previous.NewNotices),
            ["closedNotices"] = CalculateChange(current.ClosedNotices, previous.ClosedNotices),
            ["overdueNotices"] = CalculateChange(current.OverdueNotices, previous.OverdueNotices),
            ["totalDemand"] = CalculateChange(current.TotalDemand, previous.TotalDemand),
            ["averageResolutionHours"] = CalculateChange(current.AverageResolutionHours, previous.AverageResolutionHours),
            ["medianResolutionHours"] = CalculateChange(current.MedianResolutionHours, previous.MedianResolutionHours),
            ["slaComplianceRate"] = current.SlaComplianceRate - previous.SlaComplianceRate, // Absolute diff for rates
            ["tasksCompleted"] = CalculateChange(current.TasksCompleted, previous.TasksCompleted),
            ["commentsAdded"] = CalculateChange(current.CommentsAdded, previous.CommentsAdded),
            ["uniqueAssignees"] = CalculateChange(current.UniqueAssignees, previous.UniqueAssignees),
            ["averageNoticesPerAssignee"] = CalculateChange(current.AverageNoticesPerAssignee, previous.AverageNoticesPerAssignee)
        };
    }

    private static decimal CalculateChange(decimal current, decimal previous)
    {
        if (previous == 0)
        {
            return current > 0 ? 100m : 0m; // 100% increase if from zero, 0 if both zero
        }
        return Math.Round((current - previous) / previous * 100, 2);
    }

    private static decimal CalculateChange(int current, int previous)
    {
        return CalculateChange((decimal)current, (decimal)previous);
    }

    private static Dictionary<string, string> DetermineTrends(Dictionary<string, decimal> changePercent)
    {
        var trends = new Dictionary<string, string>();

        foreach (var (metric, change) in changePercent)
        {
            trends[metric] = change switch
            {
                > SignificantChangeThreshold => "up",
                < -SignificantChangeThreshold => "down",
                _ => "stable"
            };
        }

        // For some metrics, "down" is good (e.g., overdue notices, resolution time)
        // Add a secondary indicator for context
        var inverseMetrics = new HashSet<string>
        {
            "overdueNotices",
            "averageResolutionHours",
            "medianResolutionHours"
        };

        foreach (var metric in inverseMetrics)
        {
            if (trends.TryGetValue(metric, out var trend))
            {
                // Keep the trend direction but context is inverse
                // Frontend can interpret: down on overdue = good
                trends[$"{metric}_positive"] = trend == "down" ? "true" : trend == "up" ? "false" : "neutral";
            }
        }

        return trends;
    }

    private static decimal GetMedian(List<decimal> values)
    {
        if (values.Count == 0) return 0;

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }
}
