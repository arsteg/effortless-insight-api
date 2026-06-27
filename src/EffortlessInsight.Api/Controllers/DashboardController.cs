using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Dashboard API endpoints providing chart-ready data for analytics visualization.
/// All data is scoped to the current user's organization.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<DashboardController> _logger;

    // Color palettes for charts
    private static readonly Dictionary<string, string> StatusColors = new()
    {
        ["pending"] = "#f59e0b",      // Amber
        ["in_progress"] = "#3b82f6",  // Blue
        ["under_review"] = "#8b5cf6", // Purple
        ["responded"] = "#10b981",    // Green
        ["closed"] = "#6b7280",       // Gray
        ["escalated"] = "#ef4444"     // Red
    };

    private static readonly Dictionary<string, string> TaskStatusColors = new()
    {
        ["todo"] = "#f59e0b",
        ["in_progress"] = "#3b82f6",
        ["done"] = "#10b981",
        ["blocked"] = "#ef4444",
        ["on_hold"] = "#6b7280"
    };

    private static readonly Dictionary<string, string> PriorityColors = new()
    {
        ["critical"] = "#dc2626",
        ["high"] = "#f97316",
        ["medium"] = "#eab308",
        ["low"] = "#22c55e"
    };

    public DashboardController(
        ApplicationDbContext dbContext,
        ICurrentOrganizationService orgService,
        ILogger<DashboardController> logger)
    {
        _dbContext = dbContext;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue("sub")!);

    private Guid GetOrganizationId() =>
        _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");

    // ==========================================================================
    // Overview
    // ==========================================================================

    /// <summary>
    /// Get comprehensive dashboard overview with key metrics
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(DashboardOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromQuery] string? period = "30d")
    {
        var orgId = GetOrganizationId();
        var (startDate, endDate) = ParsePeriod(period);

        var metrics = await GetMetricsAsync(orgId, startDate, endDate);
        var noticesByStatus = await GetNoticesByStatusInternalAsync(orgId, startDate, endDate);
        var taskSummary = await GetTaskSummaryInternalAsync(orgId, startDate, endDate);
        var recentActivity = await GetRecentActivityAsync(orgId, 10);

        return Ok(new DashboardOverviewResponse(
            Metrics: metrics,
            NoticesByStatus: noticesByStatus,
            TaskSummary: taskSummary,
            RecentActivity: recentActivity
        ));
    }

    // ==========================================================================
    // Notice Charts
    // ==========================================================================

    /// <summary>
    /// Get notice counts grouped by status (pie/doughnut chart)
    /// </summary>
    [HttpGet("notices/by-status")]
    [ProducesResponseType(typeof(NoticesByStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNoticesByStatus([FromQuery] string? period = "all")
    {
        var orgId = GetOrganizationId();
        var (startDate, endDate) = ParsePeriod(period);

        var result = await GetNoticesByStatusInternalAsync(orgId, startDate, endDate);
        return Ok(result);
    }

    /// <summary>
    /// Get notice counts grouped by type (bar chart)
    /// </summary>
    [HttpGet("notices/by-type")]
    [ProducesResponseType(typeof(NoticesByTypeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNoticesByType([FromQuery] string? period = "all")
    {
        var orgId = GetOrganizationId();
        var (startDate, endDate) = ParsePeriod(period);

        var query = _dbContext.Notices
            .Where(n => n.OrganizationId == orgId);

        if (startDate.HasValue)
            query = query.Where(n => n.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(n => n.CreatedAt <= endDate.Value);

        var typeGroups = await query
            .GroupBy(n => n.NoticeType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToListAsync();

        var total = typeGroups.Sum(g => g.Count);
        var details = typeGroups.Select(g => new NoticeTypeCount(
            Type: g.Type,
            TypeLabel: FormatNoticeType(g.Type),
            Count: g.Count,
            Percentage: total > 0 ? Math.Round((decimal)g.Count / total * 100, 1) : 0
        )).ToList();

        var chartData = new BarChartData(
            Labels: details.Select(d => d.TypeLabel).ToList(),
            Datasets: new List<BarChartDataset>
            {
                new BarChartDataset(
                    Label: "Notices",
                    Data: details.Select(d => (decimal)d.Count).ToList(),
                    BackgroundColor: "#3b82f6"
                )
            }
        );

        return Ok(new NoticesByTypeResponse(ChartData: chartData, Details: details));
    }

    /// <summary>
    /// Get notice creation timeline (line chart)
    /// </summary>
    [HttpGet("notices/timeline")]
    [ProducesResponseType(typeof(NoticeTimelineResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNoticeTimeline(
        [FromQuery] string? period = "30d",
        [FromQuery] string? groupBy = "day")
    {
        var orgId = GetOrganizationId();
        var (startDate, endDate) = ParsePeriod(period);

        var actualStart = startDate ?? DateTime.UtcNow.AddDays(-30);
        var actualEnd = endDate ?? DateTime.UtcNow;

        var notices = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId &&
                        n.CreatedAt >= actualStart &&
                        n.CreatedAt <= actualEnd)
            .Select(n => n.CreatedAt)
            .ToListAsync();

        var groupedData = groupBy?.ToLower() switch
        {
            "week" => GroupByWeek(notices, actualStart, actualEnd),
            "month" => GroupByMonth(notices, actualStart, actualEnd),
            _ => GroupByDay(notices, actualStart, actualEnd)
        };

        var chartData = new LineChartData(
            Labels: groupedData.Select(d => d.Label).ToList(),
            Datasets: new List<LineChartDataset>
            {
                new LineChartDataset(
                    Label: "Notices Created",
                    Data: groupedData.Select(d => d.Value).ToList(),
                    BorderColor: "#3b82f6",
                    BackgroundColor: "rgba(59, 130, 246, 0.1)",
                    Fill: true
                )
            }
        );

        var totalNotices = notices.Count;
        var periodDays = (actualEnd - actualStart).Days;
        var avgPerDay = periodDays > 0 ? Math.Round((decimal)totalNotices / periodDays, 2) : 0;

        return Ok(new NoticeTimelineResponse(
            ChartData: chartData,
            Period: period ?? "30d",
            TotalNotices: totalNotices,
            AveragePerPeriod: avgPerDay
        ));
    }

    // ==========================================================================
    // Task Charts
    // ==========================================================================

    /// <summary>
    /// Get task summary with completion rates (multiple charts)
    /// </summary>
    [HttpGet("tasks/summary")]
    [ProducesResponseType(typeof(TaskSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaskSummary([FromQuery] string? period = "30d")
    {
        var orgId = GetOrganizationId();
        var (startDate, endDate) = ParsePeriod(period);

        var result = await GetTaskSummaryInternalAsync(orgId, startDate, endDate);
        return Ok(result);
    }

    // ==========================================================================
    // Team Activity
    // ==========================================================================

    /// <summary>
    /// Get team activity metrics
    /// </summary>
    [HttpGet("team/activity")]
    [ProducesResponseType(typeof(TeamActivityResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamActivity([FromQuery] string? period = "30d")
    {
        var orgId = GetOrganizationId();
        var (startDate, endDate) = ParsePeriod(period);

        var actualStart = startDate ?? DateTime.UtcNow.AddDays(-30);
        var actualEnd = endDate ?? DateTime.UtcNow;

        // Get team members
        var members = await _dbContext.OrganizationMembers
            .Include(m => m.User)
            .Where(m => m.OrganizationId == orgId && m.Status == "active")
            .ToListAsync();

        var memberActivities = new List<TeamMemberActivity>();

        foreach (var member in members)
        {
            var tasksCompleted = await _dbContext.Tasks
                .Where(t => t.Notice.OrganizationId == orgId &&
                            t.CompletedById == member.UserId &&
                            t.CompletedAt >= actualStart &&
                            t.CompletedAt <= actualEnd)
                .CountAsync();

            var commentsAdded = await _dbContext.Comments
                .Where(c => c.Notice.OrganizationId == orgId &&
                            c.UserId == member.UserId &&
                            c.CreatedAt >= actualStart &&
                            c.CreatedAt <= actualEnd)
                .CountAsync();

            var documentsUploaded = await _dbContext.Attachments
                .Where(a => a.Notice != null &&
                            a.Notice.OrganizationId == orgId &&
                            a.UploadedById == member.UserId &&
                            a.CreatedAt >= actualStart &&
                            a.CreatedAt <= actualEnd)
                .CountAsync();

            var activityScore = tasksCompleted * 3 + commentsAdded * 1 + documentsUploaded * 2;

            memberActivities.Add(new TeamMemberActivity(
                UserId: member.UserId,
                UserName: member.User?.Name ?? "Unknown",
                AvatarUrl: member.User?.AvatarUrl,
                TasksCompleted: tasksCompleted,
                CommentsAdded: commentsAdded,
                DocumentsUploaded: documentsUploaded,
                ActivityScore: activityScore
            ));
        }

        var topContributors = memberActivities
            .OrderByDescending(m => m.ActivityScore)
            .Take(10)
            .ToList();

        // Activity by member bar chart
        var activityByMember = new BarChartData(
            Labels: topContributors.Select(m => m.UserName).ToList(),
            Datasets: new List<BarChartDataset>
            {
                new BarChartDataset(
                    Label: "Tasks Completed",
                    Data: topContributors.Select(m => (decimal)m.TasksCompleted).ToList(),
                    BackgroundColor: "#10b981"
                ),
                new BarChartDataset(
                    Label: "Comments",
                    Data: topContributors.Select(m => (decimal)m.CommentsAdded).ToList(),
                    BackgroundColor: "#3b82f6"
                ),
                new BarChartDataset(
                    Label: "Documents",
                    Data: topContributors.Select(m => (decimal)m.DocumentsUploaded).ToList(),
                    BackgroundColor: "#f59e0b"
                )
            }
        );

        // Activity over time
        var activityLogs = await _dbContext.ActivityLogs
            .Where(a => a.OrganizationId == orgId &&
                        a.CreatedAt >= actualStart &&
                        a.CreatedAt <= actualEnd)
            .Select(a => a.CreatedAt)
            .ToListAsync();

        var timelineData = GroupByDay(activityLogs, actualStart, actualEnd);

        var activityOverTime = new LineChartData(
            Labels: timelineData.Select(d => d.Label).ToList(),
            Datasets: new List<LineChartDataset>
            {
                new LineChartDataset(
                    Label: "Activities",
                    Data: timelineData.Select(d => d.Value).ToList(),
                    BorderColor: "#8b5cf6",
                    BackgroundColor: "rgba(139, 92, 246, 0.1)",
                    Fill: true
                )
            }
        );

        return Ok(new TeamActivityResponse(
            ActivityByMember: activityByMember,
            ActivityOverTime: activityOverTime,
            TopContributors: topContributors
        ));
    }

    // ==========================================================================
    // Workflow Charts
    // ==========================================================================

    /// <summary>
    /// Get workflow stage distribution
    /// </summary>
    [HttpGet("workflows/stage-distribution")]
    [ProducesResponseType(typeof(WorkflowStageDistributionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkflowStageDistribution()
    {
        var orgId = GetOrganizationId();

        var stageGroups = await _dbContext.NoticeWorkflowInstances
            .Include(w => w.CurrentStage)
            .Where(w => w.Notice.OrganizationId == orgId &&
                        w.Status == "active")
            .GroupBy(w => new { w.CurrentStageId, w.CurrentStage.Name, w.CurrentStage.StageKey })
            .Select(g => new
            {
                StageKey = g.Key.StageKey,
                StageName = g.Key.Name,
                Count = g.Count()
            })
            .OrderByDescending(g => g.Count)
            .ToListAsync();

        var total = stageGroups.Sum(g => g.Count);

        var details = stageGroups.Select((g, index) => new WorkflowStageCount(
            StageKey: g.StageKey,
            StageName: g.StageName,
            Count: g.Count,
            Percentage: total > 0 ? Math.Round((decimal)g.Count / total * 100, 1) : 0,
            Color: GetStageColor(index)
        )).ToList();

        var chartData = new PieChartData(
            Labels: details.Select(d => d.StageName).ToList(),
            Data: details.Select(d => (decimal)d.Count).ToList(),
            Colors: details.Select(d => d.Color).ToList(),
            Total: total
        );

        return Ok(new WorkflowStageDistributionResponse(
            ChartData: chartData,
            Details: details,
            TotalActiveWorkflows: total
        ));
    }

    // ==========================================================================
    // Helper Methods
    // ==========================================================================

    private async Task<DashboardOverviewMetrics> GetMetricsAsync(Guid orgId, DateTime? startDate, DateTime? endDate)
    {
        var now = DateTime.UtcNow;
        var thisMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonthEnd = thisMonthStart.AddSeconds(-1);

        var totalNotices = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId)
            .CountAsync();

        var activeNotices = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId &&
                        n.Status != "closed" && n.Status != "responded")
            .CountAsync();

        var pendingTasks = await _dbContext.Tasks
            .Where(t => t.Notice.OrganizationId == orgId &&
                        t.Status == TaskStatusValues.Todo)
            .CountAsync();

        var overdueTasks = await _dbContext.Tasks
            .Where(t => t.Notice.OrganizationId == orgId &&
                        t.DueDate < now &&
                        t.Status != TaskStatusValues.Done)
            .CountAsync();

        var teamMembers = await _dbContext.OrganizationMembers
            .Where(m => m.OrganizationId == orgId && m.Status == "active")
            .CountAsync();

        var closedNotices = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId &&
                        (n.Status == "closed" || n.Status == "responded"))
            .CountAsync();

        var completedTasks = await _dbContext.Tasks
            .Where(t => t.Notice.OrganizationId == orgId &&
                        t.Status == TaskStatusValues.Done)
            .CountAsync();

        var totalTasks = await _dbContext.Tasks
            .Where(t => t.Notice.OrganizationId == orgId)
            .CountAsync();

        var noticesThisMonth = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId && n.CreatedAt >= thisMonthStart)
            .CountAsync();

        var noticesLastMonth = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId &&
                        n.CreatedAt >= lastMonthStart &&
                        n.CreatedAt <= lastMonthEnd)
            .CountAsync();

        var growthPercent = noticesLastMonth > 0
            ? Math.Round((decimal)(noticesThisMonth - noticesLastMonth) / noticesLastMonth * 100, 1)
            : (noticesThisMonth > 0 ? 100 : 0);

        return new DashboardOverviewMetrics(
            TotalNotices: totalNotices,
            ActiveNotices: activeNotices,
            PendingTasks: pendingTasks,
            OverdueTasks: overdueTasks,
            TeamMembers: teamMembers,
            NoticeResolutionRate: totalNotices > 0 ? Math.Round((decimal)closedNotices / totalNotices * 100, 1) : 0,
            TaskCompletionRate: totalTasks > 0 ? Math.Round((decimal)completedTasks / totalTasks * 100, 1) : 0,
            NoticesThisMonth: noticesThisMonth,
            NoticesLastMonth: noticesLastMonth,
            NoticeGrowthPercent: growthPercent
        );
    }

    private async Task<NoticesByStatusResponse> GetNoticesByStatusInternalAsync(
        Guid orgId, DateTime? startDate, DateTime? endDate)
    {
        var query = _dbContext.Notices.Where(n => n.OrganizationId == orgId);

        if (startDate.HasValue)
            query = query.Where(n => n.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(n => n.CreatedAt <= endDate.Value);

        var statusGroups = await query
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = statusGroups.Sum(g => g.Count);

        var details = statusGroups.Select(g => new NoticeStatusCount(
            Status: g.Status,
            Count: g.Count,
            Percentage: total > 0 ? Math.Round((decimal)g.Count / total * 100, 1) : 0,
            Color: StatusColors.GetValueOrDefault(g.Status, "#6b7280")
        )).OrderByDescending(d => d.Count).ToList();

        var chartData = new PieChartData(
            Labels: details.Select(d => FormatStatus(d.Status)).ToList(),
            Data: details.Select(d => (decimal)d.Count).ToList(),
            Colors: details.Select(d => d.Color).ToList(),
            Total: total
        );

        return new NoticesByStatusResponse(ChartData: chartData, Details: details);
    }

    private async Task<TaskSummaryResponse> GetTaskSummaryInternalAsync(
        Guid orgId, DateTime? startDate, DateTime? endDate)
    {
        var query = _dbContext.Tasks.Where(t => t.Notice.OrganizationId == orgId);

        if (startDate.HasValue)
            query = query.Where(t => t.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(t => t.CreatedAt <= endDate.Value);

        var tasks = await query
            .Select(t => new { t.Status, t.Priority, t.DueDate })
            .ToListAsync();

        var now = DateTime.UtcNow;
        var totalTasks = tasks.Count;
        var completedTasks = tasks.Count(t => t.Status == TaskStatusValues.Done);
        var inProgressTasks = tasks.Count(t => t.Status == TaskStatusValues.InProgress);
        var overdueTasks = tasks.Count(t => t.DueDate < now && t.Status != TaskStatusValues.Done);

        // Status chart
        var statusGroups = tasks.GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var statusChart = new PieChartData(
            Labels: statusGroups.Select(g => FormatTaskStatus(g.Status)).ToList(),
            Data: statusGroups.Select(g => (decimal)g.Count).ToList(),
            Colors: statusGroups.Select(g => TaskStatusColors.GetValueOrDefault(g.Status, "#6b7280")).ToList(),
            Total: totalTasks
        );

        // Priority chart
        var priorityGroups = tasks.GroupBy(t => t.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var priorityChart = new PieChartData(
            Labels: priorityGroups.Select(g => FormatPriority(g.Priority)).ToList(),
            Data: priorityGroups.Select(g => (decimal)g.Count).ToList(),
            Colors: priorityGroups.Select(g => PriorityColors.GetValueOrDefault(g.Priority, "#6b7280")).ToList(),
            Total: totalTasks
        );

        return new TaskSummaryResponse(
            TotalTasks: totalTasks,
            CompletedTasks: completedTasks,
            OverdueTasks: overdueTasks,
            InProgressTasks: inProgressTasks,
            CompletionRate: totalTasks > 0 ? Math.Round((decimal)completedTasks / totalTasks * 100, 1) : 0,
            StatusChart: statusChart,
            PriorityChart: priorityChart
        );
    }

    private async Task<List<RecentActivityItem>> GetRecentActivityAsync(Guid orgId, int limit)
    {
        var activities = await _dbContext.ActivityLogs
            .Include(a => a.Actor)
            .Where(a => a.OrganizationId == orgId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return activities.Select(a => new RecentActivityItem(
            Timestamp: a.CreatedAt,
            ActivityType: a.ActivityType,
            Message: a.Message,
            ActorId: a.ActorId,
            ActorName: a.Actor?.Name,
            ActorAvatarUrl: a.Actor?.AvatarUrl,
            EntityId: a.NoticeId,
            EntityType: a.NoticeId.HasValue ? "notice" : null
        )).ToList();
    }

    private static (DateTime? StartDate, DateTime? EndDate) ParsePeriod(string? period)
    {
        if (string.IsNullOrEmpty(period) || period == "all")
            return (null, null);

        var now = DateTime.UtcNow;

        return period.ToLower() switch
        {
            "7d" => (now.AddDays(-7), now),
            "30d" => (now.AddDays(-30), now),
            "90d" => (now.AddDays(-90), now),
            "1y" => (now.AddYears(-1), now),
            "ytd" => (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), now),
            _ => (null, null)
        };
    }

    private static List<(string Label, decimal Value)> GroupByDay(
        List<DateTime> dates, DateTime start, DateTime end)
    {
        var result = new List<(string Label, decimal Value)>();
        var current = start.Date;

        while (current <= end.Date)
        {
            var count = dates.Count(d => d.Date == current);
            result.Add((current.ToString("MMM dd"), count));
            current = current.AddDays(1);
        }

        return result;
    }

    private static List<(string Label, decimal Value)> GroupByWeek(
        List<DateTime> dates, DateTime start, DateTime end)
    {
        var result = new List<(string Label, decimal Value)>();
        var current = start.Date;

        while (current <= end.Date)
        {
            var weekEnd = current.AddDays(6);
            if (weekEnd > end.Date) weekEnd = end.Date;

            var count = dates.Count(d => d.Date >= current && d.Date <= weekEnd);
            result.Add(($"{current:MMM dd} - {weekEnd:MMM dd}", count));
            current = current.AddDays(7);
        }

        return result;
    }

    private static List<(string Label, decimal Value)> GroupByMonth(
        List<DateTime> dates, DateTime start, DateTime end)
    {
        var result = new List<(string Label, decimal Value)>();
        var current = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        while (current <= end)
        {
            var monthEnd = current.AddMonths(1).AddSeconds(-1);
            var count = dates.Count(d => d >= current && d <= monthEnd);
            result.Add((current.ToString("MMM yyyy"), count));
            current = current.AddMonths(1);
        }

        return result;
    }

    private static string FormatStatus(string status) => status switch
    {
        "pending" => "Pending",
        "in_progress" => "In Progress",
        "under_review" => "Under Review",
        "responded" => "Responded",
        "closed" => "Closed",
        "escalated" => "Escalated",
        _ => status
    };

    private static string FormatTaskStatus(string status) => status switch
    {
        "todo" => "To Do",
        "in_progress" => "In Progress",
        "done" => "Done",
        "blocked" => "Blocked",
        "on_hold" => "On Hold",
        _ => status
    };

    private static string FormatPriority(string priority) => priority switch
    {
        "critical" => "Critical",
        "high" => "High",
        "medium" => "Medium",
        "low" => "Low",
        _ => priority
    };

    private static string FormatNoticeType(string type) => type switch
    {
        "gstr1_mismatch" => "GSTR-1 Mismatch",
        "gstr3b_mismatch" => "GSTR-3B Mismatch",
        "itc_reversal" => "ITC Reversal",
        "show_cause" => "Show Cause",
        "demand_order" => "Demand Order",
        "assessment" => "Assessment",
        _ => type.Replace("_", " ")
    };

    private static string GetStageColor(int index)
    {
        var colors = new[] { "#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6", "#ec4899", "#06b6d4" };
        return colors[index % colors.Length];
    }
}
