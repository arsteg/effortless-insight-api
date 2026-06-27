using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Analytics;

/// <summary>
/// Service for notice analytics (GAP-RPT-001).
/// </summary>
public interface INoticeAnalyticsService
{
    /// <summary>
    /// Gets summary analytics for notices in the organization.
    /// </summary>
    Task<NoticeAnalyticsSummary> GetSummaryAsync(Guid orgId, DateRange range, CancellationToken ct);

    /// <summary>
    /// Gets time series trend data for a specific metric.
    /// </summary>
    Task<List<TimeSeriesDataPoint>> GetTrendAsync(Guid orgId, string metric, DateRange range, string interval, CancellationToken ct);

    /// <summary>
    /// Gets notice breakdown by category.
    /// </summary>
    Task<List<CategoryBreakdown>> GetCategoryBreakdownAsync(Guid orgId, DateRange range, CancellationToken ct);

    /// <summary>
    /// Gets resolution metrics including SLA compliance.
    /// </summary>
    Task<ResolutionMetrics> GetResolutionMetricsAsync(Guid orgId, DateRange range, CancellationToken ct);
}

public class NoticeAnalyticsService : INoticeAnalyticsService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<NoticeAnalyticsService> _logger;

    public NoticeAnalyticsService(
        ApplicationDbContext context,
        ILogger<NoticeAnalyticsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<NoticeAnalyticsSummary> GetSummaryAsync(Guid orgId, DateRange range, CancellationToken ct)
    {
        var startDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);

        // Get notices in the date range
        var notices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.CreatedAt >= startDateTime && n.CreatedAt <= endDateTime)
            .Select(n => new
            {
                n.Id,
                n.Status,
                n.Priority,
                n.NoticeCategory,
                n.ResponseDeadline,
                n.TotalDemand,
                n.CreatedAt,
                n.UpdatedAt,
                ClosedAt = n.Status == NoticeStatus.Closed ? (DateTime?)n.UpdatedAt : null
            })
            .ToListAsync(ct);

        // Calculate status breakdown
        var byStatus = new StatusBreakdown(
            Uploaded: notices.Count(n => n.Status == NoticeStatus.Uploaded),
            Processing: notices.Count(n => n.Status == NoticeStatus.Processing),
            Analyzed: notices.Count(n => n.Status == NoticeStatus.Analyzed),
            InProgress: notices.Count(n => n.Status == NoticeStatus.InProgress),
            Responded: notices.Count(n => n.Status == NoticeStatus.Responded),
            Closed: notices.Count(n => n.Status == NoticeStatus.Closed),
            Archived: notices.Count(n => n.Status == NoticeStatus.Archived),
            Failed: notices.Count(n => n.Status == NoticeStatus.Failed)
        );

        // Calculate priority breakdown
        var byPriority = new PriorityBreakdown(
            Low: notices.Count(n => n.Priority == NoticePriority.Low),
            Medium: notices.Count(n => n.Priority == NoticePriority.Medium),
            High: notices.Count(n => n.Priority == NoticePriority.High),
            Critical: notices.Count(n => n.Priority == NoticePriority.Critical)
        );

        // Calculate average resolution time for closed notices
        var closedNotices = notices.Where(n => n.ClosedAt.HasValue).ToList();
        var avgResolutionHours = closedNotices.Count > 0
            ? (decimal)closedNotices.Average(n => (n.ClosedAt!.Value - n.CreatedAt).TotalHours)
            : 0;

        // Calculate deadline metrics
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekEnd = today.AddDays(7);
        var overdueCount = notices.Count(n =>
            n.ResponseDeadline.HasValue &&
            n.ResponseDeadline.Value < today &&
            n.Status != NoticeStatus.Closed &&
            n.Status != NoticeStatus.Archived);
        var dueTodayCount = notices.Count(n =>
            n.ResponseDeadline.HasValue &&
            n.ResponseDeadline.Value == today &&
            n.Status != NoticeStatus.Closed);
        var dueThisWeekCount = notices.Count(n =>
            n.ResponseDeadline.HasValue &&
            n.ResponseDeadline.Value >= today &&
            n.ResponseDeadline.Value <= weekEnd &&
            n.Status != NoticeStatus.Closed);

        // Calculate total demand
        var totalDemand = notices.Sum(n => n.TotalDemand ?? 0);

        // Get comparison metrics with previous period
        var comparison = await GetComparisonMetricsAsync(orgId, range, ct);

        return new NoticeAnalyticsSummary(
            TotalNotices: notices.Count,
            ByStatus: byStatus,
            ByPriority: byPriority,
            AverageResolutionTimeHours: Math.Round(avgResolutionHours, 2),
            OverdueCount: overdueCount,
            DueTodayCount: dueTodayCount,
            DueThisWeekCount: dueThisWeekCount,
            TotalDemandAmount: totalDemand,
            Comparison: comparison
        );
    }

    public async Task<List<TimeSeriesDataPoint>> GetTrendAsync(
        Guid orgId,
        string metric,
        DateRange range,
        string interval,
        CancellationToken ct)
    {
        var startDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);

        var notices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.CreatedAt >= startDateTime && n.CreatedAt <= endDateTime)
            .Select(n => new
            {
                n.Id,
                n.Status,
                n.Priority,
                n.TotalDemand,
                n.CreatedAt,
                n.UpdatedAt
            })
            .ToListAsync(ct);

        var dataPoints = new List<TimeSeriesDataPoint>();
        var currentDate = range.StartDate;

        while (currentDate <= range.EndDate)
        {
            var (periodStart, periodEnd, nextDate) = GetPeriodBounds(currentDate, interval, range.EndDate);

            var periodNotices = notices.Where(n =>
                DateOnly.FromDateTime(n.CreatedAt) >= periodStart &&
                DateOnly.FromDateTime(n.CreatedAt) <= periodEnd).ToList();

            decimal value = metric.ToLowerInvariant() switch
            {
                "count" or "notices" => periodNotices.Count,
                "closed" => periodNotices.Count(n => n.Status == NoticeStatus.Closed),
                "new" or "created" => periodNotices.Count,
                "demand" or "amount" => periodNotices.Sum(n => n.TotalDemand ?? 0),
                "high_priority" => periodNotices.Count(n =>
                    n.Priority == NoticePriority.High || n.Priority == NoticePriority.Critical),
                _ => periodNotices.Count
            };

            dataPoints.Add(new TimeSeriesDataPoint(
                Date: periodStart,
                Value: value,
                Label: GetPeriodLabel(periodStart, interval)
            ));

            currentDate = nextDate;
        }

        return dataPoints;
    }

    public async Task<List<CategoryBreakdown>> GetCategoryBreakdownAsync(Guid orgId, DateRange range, CancellationToken ct)
    {
        var startDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);

        var notices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.CreatedAt >= startDateTime && n.CreatedAt <= endDateTime)
            .Select(n => new
            {
                n.Id,
                Category = n.NoticeCategory ?? "uncategorized",
                n.TotalDemand,
                n.Status,
                n.CreatedAt,
                n.UpdatedAt
            })
            .ToListAsync(ct);

        var totalCount = notices.Count;
        if (totalCount == 0)
            return [];

        var categories = notices
            .GroupBy(n => n.Category)
            .Select(g =>
            {
                var closedNotices = g.Where(n => n.Status == NoticeStatus.Closed).ToList();
                var avgResolutionHours = closedNotices.Count > 0
                    ? (decimal)closedNotices.Average(n => ((n.UpdatedAt - n.CreatedAt) ?? TimeSpan.Zero).TotalHours)
                    : 0;

                return new CategoryBreakdown(
                    Category: g.Key,
                    Count: g.Count(),
                    Percentage: Math.Round((decimal)g.Count() / totalCount * 100, 2),
                    TotalDemand: g.Sum(n => n.TotalDemand ?? 0),
                    AverageResolutionHours: Math.Round(avgResolutionHours, 2)
                );
            })
            .OrderByDescending(c => c.Count)
            .ToList();

        return categories;
    }

    public async Task<ResolutionMetrics> GetResolutionMetricsAsync(Guid orgId, DateRange range, CancellationToken ct)
    {
        var startDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);

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
                n.CreatedAt,
                n.UpdatedAt
            })
            .ToListAsync(ct);

        var closedNotices = notices
            .Where(n => n.Status == NoticeStatus.Closed || n.Status == NoticeStatus.Archived)
            .ToList();

        var openNotices = notices
            .Where(n => n.Status != NoticeStatus.Closed && n.Status != NoticeStatus.Archived)
            .ToList();

        // Calculate resolution times
        var resolutionHours = closedNotices
            .Select(n => (decimal)((n.UpdatedAt - n.CreatedAt) ?? TimeSpan.Zero).TotalHours)
            .OrderBy(h => h)
            .ToList();

        var avgHours = resolutionHours.Count > 0 ? resolutionHours.Average() : 0;
        var medianHours = resolutionHours.Count > 0 ? GetMedian(resolutionHours) : 0;
        var minHours = resolutionHours.Count > 0 ? resolutionHours.Min() : 0;
        var maxHours = resolutionHours.Count > 0 ? resolutionHours.Max() : 0;

        // Calculate SLA compliance (notices closed before deadline)
        var noticesWithDeadline = closedNotices.Where(n => n.ResponseDeadline.HasValue).ToList();
        var slaMetCount = noticesWithDeadline.Count(n =>
            n.UpdatedAt.HasValue && DateOnly.FromDateTime(n.UpdatedAt.Value) <= n.ResponseDeadline!.Value);
        var slaBreachedCount = noticesWithDeadline.Count - slaMetCount;
        var slaComplianceRate = noticesWithDeadline.Count > 0
            ? Math.Round((decimal)slaMetCount / noticesWithDeadline.Count * 100, 2)
            : 100m;

        // Resolution by priority
        var byPriority = NoticePriority.All
            .Select(priority =>
            {
                var priorityNotices = closedNotices.Where(n => n.Priority == priority).ToList();
                var priorityWithDeadline = priorityNotices.Where(n => n.ResponseDeadline.HasValue).ToList();
                var prioritySlaMet = priorityWithDeadline.Count(n =>
                    n.UpdatedAt.HasValue && DateOnly.FromDateTime(n.UpdatedAt.Value) <= n.ResponseDeadline!.Value);

                return new ResolutionByPriority(
                    Priority: priority,
                    AverageHours: priorityNotices.Count > 0
                        ? Math.Round((decimal)priorityNotices.Average(n => ((n.UpdatedAt - n.CreatedAt) ?? TimeSpan.Zero).TotalHours), 2)
                        : 0,
                    Count: priorityNotices.Count,
                    SlaComplianceRate: priorityWithDeadline.Count > 0
                        ? Math.Round((decimal)prioritySlaMet / priorityWithDeadline.Count * 100, 2)
                        : 100m
                );
            })
            .Where(r => r.Count > 0)
            .ToList();

        // Resolution by category
        var byCategory = closedNotices
            .GroupBy(n => n.Category)
            .Select(g => new ResolutionByCategory(
                Category: g.Key,
                AverageHours: Math.Round((decimal)g.Average(n => ((n.UpdatedAt - n.CreatedAt) ?? TimeSpan.Zero).TotalHours), 2),
                Count: g.Count()
            ))
            .OrderByDescending(r => r.Count)
            .Take(10)
            .ToList();

        return new ResolutionMetrics(
            AverageTimeToCloseHours: Math.Round(avgHours, 2),
            MedianTimeToCloseHours: Math.Round(medianHours, 2),
            MinTimeToCloseHours: Math.Round(minHours, 2),
            MaxTimeToCloseHours: Math.Round(maxHours, 2),
            TotalClosed: closedNotices.Count,
            TotalOpen: openNotices.Count,
            SlaComplianceRate: slaComplianceRate,
            SlaMetCount: slaMetCount,
            SlaBreachedCount: slaBreachedCount,
            ByPriority: byPriority,
            ByCategory: byCategory
        );
    }

    private async Task<ComparisonMetrics?> GetComparisonMetricsAsync(Guid orgId, DateRange range, CancellationToken ct)
    {
        // Calculate previous period with same duration
        var periodDays = range.EndDate.DayNumber - range.StartDate.DayNumber + 1;
        var prevEndDate = range.StartDate.AddDays(-1);
        var prevStartDate = prevEndDate.AddDays(-periodDays + 1);

        var currentStartDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var currentEndDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);
        var prevStartDateTime = prevStartDate.ToDateTime(TimeOnly.MinValue);
        var prevEndDateTime = prevEndDate.ToDateTime(TimeOnly.MaxValue);

        // Get current period data
        var currentNotices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.CreatedAt >= currentStartDateTime && n.CreatedAt <= currentEndDateTime)
            .Select(n => new { n.Status, n.CreatedAt, n.UpdatedAt })
            .ToListAsync(ct);

        // Get previous period data
        var prevNotices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.CreatedAt >= prevStartDateTime && n.CreatedAt <= prevEndDateTime)
            .Select(n => new { n.Status, n.CreatedAt, n.UpdatedAt })
            .ToListAsync(ct);

        var currentNew = currentNotices.Count;
        var prevNew = prevNotices.Count;

        var currentClosed = currentNotices.Count(n => n.Status == NoticeStatus.Closed);
        var prevClosed = prevNotices.Count(n => n.Status == NoticeStatus.Closed);

        var currentClosedList = currentNotices.Where(n => n.Status == NoticeStatus.Closed).ToList();
        var prevClosedList = prevNotices.Where(n => n.Status == NoticeStatus.Closed).ToList();

        var currentAvgResolution = currentClosedList.Count > 0
            ? (decimal)currentClosedList.Average(n => ((n.UpdatedAt - n.CreatedAt) ?? TimeSpan.Zero).TotalHours)
            : 0;
        var prevAvgResolution = prevClosedList.Count > 0
            ? (decimal)prevClosedList.Average(n => ((n.UpdatedAt - n.CreatedAt) ?? TimeSpan.Zero).TotalHours)
            : 0;

        return new ComparisonMetrics(
            NewNoticesChange: currentNew - prevNew,
            NewNoticesChangePercent: prevNew > 0 ? Math.Round((decimal)(currentNew - prevNew) / prevNew * 100, 2) : 0,
            ClosedNoticesChange: currentClosed - prevClosed,
            ClosedNoticesChangePercent: prevClosed > 0 ? Math.Round((decimal)(currentClosed - prevClosed) / prevClosed * 100, 2) : 0,
            AvgResolutionTimeChange: Math.Round(currentAvgResolution - prevAvgResolution, 2),
            AvgResolutionTimeChangePercent: prevAvgResolution > 0 ? Math.Round((currentAvgResolution - prevAvgResolution) / prevAvgResolution * 100, 2) : 0
        );
    }

    private static (DateOnly periodStart, DateOnly periodEnd, DateOnly nextDate) GetPeriodBounds(
        DateOnly currentDate,
        string interval,
        DateOnly rangeEnd)
    {
        return interval.ToLowerInvariant() switch
        {
            "weekly" => (
                currentDate,
                DateOnly.FromDayNumber(Math.Min(currentDate.AddDays(6).DayNumber, rangeEnd.DayNumber)),
                currentDate.AddDays(7)
            ),
            "monthly" => (
                currentDate,
                DateOnly.FromDayNumber(Math.Min(
                    new DateOnly(currentDate.Year, currentDate.Month, DateTime.DaysInMonth(currentDate.Year, currentDate.Month)).DayNumber,
                    rangeEnd.DayNumber)),
                currentDate.AddMonths(1).AddDays(-currentDate.Day + 1)
            ),
            _ => (currentDate, currentDate, currentDate.AddDays(1)) // daily
        };
    }

    private static string GetPeriodLabel(DateOnly date, string interval)
    {
        return interval.ToLowerInvariant() switch
        {
            "weekly" => $"Week of {date:MMM d}",
            "monthly" => date.ToString("MMM yyyy"),
            _ => date.ToString("MMM d")
        };
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
