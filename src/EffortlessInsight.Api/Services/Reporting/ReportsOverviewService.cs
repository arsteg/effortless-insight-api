using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Analytics;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Builds the data for the web /reports page. Reuses the dashboard, analytics and
/// compliance services so the numbers match /dashboard.
/// </summary>
public interface IReportsOverviewService
{
    Task<ReportsResponse> GetReportsAsync(Guid orgId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct);
}

public record ReportsResponse
{
    public ReportsOverview Overview { get; init; } = new();
    public List<ReportsMonthlyTrend> MonthlyTrend { get; init; } = [];
    public List<ReportsNoticeType> NoticesByType { get; init; } = [];
    public Dictionary<string, int> NoticesByStatus { get; init; } = new();
    public Dictionary<string, int> NoticesByPriority { get; init; } = new();
    public List<ReportsGstinSummary> GstinSummary { get; init; } = [];
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
}

public record ReportsOverview
{
    public int TotalNotices { get; init; }
    public int ResolvedNotices { get; init; }
    public int PendingNotices { get; init; }
    public decimal TotalDemandAmount { get; init; }
    public decimal ResolvedAmount { get; init; }
    public decimal PendingAmount { get; init; }
    public decimal AvgResolutionDays { get; init; }
    public decimal ComplianceScore { get; init; }
}

public record ReportsMonthlyTrend
{
    public string Month { get; init; } = string.Empty; // "1".."12", parsed by the web chart
    public int Year { get; init; }
    public int NoticesReceived { get; init; }
    public int NoticesResolved { get; init; }
    public decimal TotalDemand { get; init; }
    public decimal ComplianceScore { get; init; }
}

public record ReportsNoticeType
{
    public string Type { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal Percentage { get; init; }
    public decimal TotalDemand { get; init; }
}

public record ReportsGstinSummary
{
    public string Gstin { get; init; } = string.Empty;
    public string StateName { get; init; } = string.Empty;
    public string? TradeName { get; init; }
    public int ActiveNotices { get; init; }
    public int ResolvedNotices { get; init; }
    public decimal PendingAmount { get; init; }
    public decimal ResolvedAmount { get; init; }
}

public class ReportsOverviewService : IReportsOverviewService
{
    // Same status groups as DashboardService
    private static readonly string[] ClosedStatuses = [NoticeStatus.Closed, NoticeStatus.Archived];
    private static readonly string[] OpenStatuses =
        [NoticeStatus.Uploaded, NoticeStatus.Processing, NoticeStatus.Analyzed, NoticeStatus.InProgress, NoticeStatus.Responded];

    private readonly ApplicationDbContext _context;
    private readonly IDashboardService _dashboardService;
    private readonly INoticeAnalyticsService _noticeAnalyticsService;
    private readonly IComplianceReportService _complianceReportService;

    public ReportsOverviewService(
        ApplicationDbContext context,
        IDashboardService dashboardService,
        INoticeAnalyticsService noticeAnalyticsService,
        IComplianceReportService complianceReportService)
    {
        _context = context;
        _dashboardService = dashboardService;
        _noticeAnalyticsService = noticeAnalyticsService;
        _complianceReportService = complianceReportService;
    }

    private record NoticeRow(Guid Id, string Status, string? NoticeType, string? Gstin,
        decimal? TotalDemand, DateTime CreatedAt, DateTime? UpdatedAt);

    public async Task<ReportsResponse> GetReportsAsync(Guid orgId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
    {
        // Counts, status, priority and demand come from the dashboard so both pages match
        var dashboard = await _dashboardService.GetDashboardMetricsAsync(orgId, startDate, endDate, ct);

        var notices = await GetNoticesAsync(orgId, startDate, endDate, ct);

        // Analytics services need a concrete range; "all time" starts at the oldest notice
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var range = new DateRange(
            startDate ?? (notices.Count > 0 ? DateOnly.FromDateTime(notices.Min(n => n.CreatedAt)) : today),
            endDate ?? today);

        var resolution = await _noticeAnalyticsService.GetResolutionMetricsAsync(orgId, range, ct);
        var compliance = await _complianceReportService.GenerateDeadlineComplianceReportAsync(orgId, range, ct);
        var compliantById = compliance.Items.ToDictionary(i => i.Id, i => i.IsCompliant);
        var closedDates = await GetClosedDatesAsync(orgId, range, ct);

        return new ReportsResponse
        {
            Overview = new ReportsOverview
            {
                TotalNotices = dashboard.Notices.Total,
                ResolvedNotices = dashboard.Notices.Closed,
                PendingNotices = dashboard.Notices.Open,
                TotalDemandAmount = dashboard.Notices.TotalDemandAmount,
                ResolvedAmount = notices.Where(n => ClosedStatuses.Contains(n.Status)).Sum(n => n.TotalDemand ?? 0),
                PendingAmount = notices.Where(n => OpenStatuses.Contains(n.Status)).Sum(n => n.TotalDemand ?? 0),
                AvgResolutionDays = Math.Round(resolution.AverageTimeToCloseHours / 24, 1),
                ComplianceScore = Math.Round(compliance.Summary.ComplianceRate)
            },
            NoticesByStatus = dashboard.Notices.ByStatus,
            NoticesByPriority = dashboard.Notices.ByPriority,
            NoticesByType = BuildTypeBreakdown(notices),
            MonthlyTrend = BuildMonthlyTrend(notices, closedDates, compliantById, range),
            GstinSummary = await BuildGstinSummaryAsync(orgId, notices, ct),
            PeriodStart = range.StartDate,
            PeriodEnd = range.EndDate
        };
    }

    private async Task<List<NoticeRow>> GetNoticesAsync(Guid orgId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
    {
        // Same filter as DashboardService.GetNoticeMetricsAsync
        var query = _context.Notices
            .Where(n => n.OrganizationId == orgId && n.DeletedAt == null);

        if (startDate.HasValue)
        {
            var filterStart = DateTime.SpecifyKind(startDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(n => n.CreatedAt >= filterStart);
        }
        if (endDate.HasValue)
        {
            var filterEnd = DateTime.SpecifyKind(endDate.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
            query = query.Where(n => n.CreatedAt <= filterEnd);
        }

        return await query
            .Select(n => new NoticeRow(n.Id, n.Status, n.NoticeType, n.Gstin, n.TotalDemand, n.CreatedAt, n.UpdatedAt))
            .ToListAsync(ct);
    }

    private static List<ReportsNoticeType> BuildTypeBreakdown(List<NoticeRow> notices)
    {
        // AI extraction and GST sync store raw text, so "drc-01" and "DRC-01 " must group together.
        // Notices with no type yet (processing or failed) go under "Unspecified" so bars add up to the total.
        return notices
            .GroupBy(n => NormalizeNoticeType(n.NoticeType))
            .Select(g => new ReportsNoticeType
            {
                Type = g.Key,
                Count = g.Count(),
                Percentage = Math.Round((decimal)g.Count() / notices.Count * 100, 1),
                TotalDemand = g.Sum(n => n.TotalDemand ?? 0)
            })
            .OrderByDescending(t => t.Count)
            .ToList();
    }

    private static string NormalizeNoticeType(string? type) =>
        string.IsNullOrWhiteSpace(type)
            ? "Unspecified"
            : Regex.Replace(type.Trim().ToUpperInvariant(), @"\s+", " ");

    private async Task<List<DateTime>> GetClosedDatesAsync(Guid orgId, DateRange range, CancellationToken ct)
    {
        // Notices closed in the period, even if they were received before it.
        // Closed time is UpdatedAt, same as NoticeAnalyticsService (there is no ClosedAt column).
        var start = DateTime.SpecifyKind(range.StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(range.EndDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        return await _context.Notices
            .Where(n => n.OrganizationId == orgId && n.DeletedAt == null)
            .Where(n => ClosedStatuses.Contains(n.Status))
            .Where(n => n.UpdatedAt >= start && n.UpdatedAt <= end)
            .Select(n => n.UpdatedAt!.Value)
            .ToListAsync(ct);
    }

    private static List<ReportsMonthlyTrend> BuildMonthlyTrend(
        List<NoticeRow> notices, List<DateTime> closedDates, Dictionary<Guid, bool> compliantById, DateRange range)
    {
        var trend = new List<ReportsMonthlyTrend>();
        var lastMonth = new DateOnly(range.EndDate.Year, range.EndDate.Month, 1);

        for (var month = new DateOnly(range.StartDate.Year, range.StartDate.Month, 1); month <= lastMonth; month = month.AddMonths(1))
        {
            var received = notices
                .Where(n => n.CreatedAt.Year == month.Year && n.CreatedAt.Month == month.Month)
                .ToList();

            var resolved = closedDates.Count(d => d.Year == month.Year && d.Month == month.Month);

            var withDeadline = received.Where(n => compliantById.ContainsKey(n.Id)).ToList();

            trend.Add(new ReportsMonthlyTrend
            {
                Month = month.Month.ToString(),
                Year = month.Year,
                NoticesReceived = received.Count,
                NoticesResolved = resolved,
                TotalDemand = received.Sum(n => n.TotalDemand ?? 0),
                // Same rule as ComplianceReportService: 100 when nothing had a deadline
                ComplianceScore = withDeadline.Count > 0
                    ? Math.Round((decimal)withDeadline.Count(n => compliantById[n.Id]) / withDeadline.Count * 100)
                    : 100m
            });
        }

        return trend;
    }

    private async Task<List<ReportsGstinSummary>> BuildGstinSummaryAsync(Guid orgId, List<NoticeRow> notices, CancellationToken ct)
    {
        var gstins = await _context.OrganizationGstins
            .Where(g => g.OrganizationId == orgId)
            .Select(g => new { g.Gstin, g.StateName, g.TradeName })
            .ToListAsync(ct);
        var detailsByGstin = gstins
            .GroupBy(g => g.Gstin.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        return notices
            .Where(n => !string.IsNullOrWhiteSpace(n.Gstin))
            // Stored GSTINs can differ in case/spacing (NoticeProcessingJob compares them case-insensitively)
            .GroupBy(n => n.Gstin!.Trim().ToUpperInvariant())
            .Select(g =>
            {
                detailsByGstin.TryGetValue(g.Key, out var details);
                return new ReportsGstinSummary
                {
                    Gstin = g.Key,
                    StateName = details?.StateName ?? string.Empty,
                    TradeName = details?.TradeName,
                    ActiveNotices = g.Count(n => OpenStatuses.Contains(n.Status)),
                    ResolvedNotices = g.Count(n => ClosedStatuses.Contains(n.Status)),
                    PendingAmount = g.Where(n => OpenStatuses.Contains(n.Status)).Sum(n => n.TotalDemand ?? 0),
                    ResolvedAmount = g.Where(n => ClosedStatuses.Contains(n.Status)).Sum(n => n.TotalDemand ?? 0)
                };
            })
            .OrderByDescending(s => s.ActiveNotices)
            .ToList();
    }
}
