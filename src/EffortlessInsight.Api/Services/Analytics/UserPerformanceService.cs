using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Analytics;

/// <summary>
/// Service for user performance metrics (GAP-RPT-002).
/// </summary>
public interface IUserPerformanceService
{
    /// <summary>
    /// Gets performance metrics for a specific user.
    /// </summary>
    Task<UserPerformanceSummary> GetUserPerformanceAsync(Guid userId, Guid orgId, DateRange range, CancellationToken ct);

    /// <summary>
    /// Gets performance metrics for all team members in an organization.
    /// </summary>
    Task<List<UserPerformanceSummary>> GetTeamPerformanceAsync(Guid orgId, DateRange range, CancellationToken ct);

    /// <summary>
    /// Gets a leaderboard ranking users by a specific metric.
    /// </summary>
    Task<List<UserRanking>> GetLeaderboardAsync(Guid orgId, string metric, DateRange range, int limit, CancellationToken ct);
}

public class UserPerformanceService : IUserPerformanceService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UserPerformanceService> _logger;

    private static readonly string[] ValidMetrics =
    [
        "tasks_completed",
        "notices_handled",
        "notices_closed",
        "sla_adherence",
        "response_time",
        "workflow_transitions",
        "comments"
    ];

    public UserPerformanceService(
        ApplicationDbContext context,
        ILogger<UserPerformanceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Helper method to convert DateOnly to UTC DateTime for PostgreSQL compatibility
    private static DateTime ToUtcDateTime(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Utc);

    public async Task<UserPerformanceSummary> GetUserPerformanceAsync(
        Guid userId,
        Guid orgId,
        DateRange range,
        CancellationToken ct)
    {
        var startDateTime = ToUtcDateTime(range.StartDate, TimeOnly.MinValue);
        var endDateTime = ToUtcDateTime(range.EndDate, TimeOnly.MaxValue);

        // Get user info
        var user = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Name, u.Email, u.AvatarUrl })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        // Get tasks completed by user
        var tasksData = await _context.Tasks
            .Where(t => t.CompletedById == userId)
            .Where(t => t.CompletedAt.HasValue && t.CompletedAt >= startDateTime && t.CompletedAt <= endDateTime)
            .Where(t => t.Notice.OrganizationId == orgId)
            .Select(t => new { t.Id, t.DueDate, t.CompletedAt })
            .ToListAsync(ct);

        var tasksCompleted = tasksData.Count;

        // Get tasks assigned to user
        var tasksAssigned = await _context.TaskAssignees
            .Where(ta => ta.UserId == userId)
            .Where(ta => ta.AssignedAt >= startDateTime && ta.AssignedAt <= endDateTime)
            .Where(ta => ta.Task.Notice.OrganizationId == orgId)
            .CountAsync(ct);

        // Get overdue tasks
        var today = DateTime.UtcNow;
        var tasksOverdue = await _context.TaskAssignees
            .Where(ta => ta.UserId == userId)
            .Where(ta => ta.Task.Notice.OrganizationId == orgId)
            .Where(ta => ta.Task.DueDate.HasValue && ta.Task.DueDate < today)
            .Where(ta => ta.Task.Status != TaskStatusValues.Done && ta.Task.Status != TaskStatusValues.Cancelled)
            .CountAsync(ct);

        // Get notices handled (assigned to user)
        var noticesHandled = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.AssignedToId == userId)
            .Where(n => n.AssignedAt.HasValue && n.AssignedAt >= startDateTime && n.AssignedAt <= endDateTime)
            .CountAsync(ct);

        // Get notices closed by user (where user is assignee and status changed to closed)
        var noticesClosed = await _context.WorkflowHistories
            .Where(wh => wh.Notice.OrganizationId == orgId)
            .Where(wh => wh.PerformedById == userId)
            .Where(wh => wh.CreatedAt >= startDateTime && wh.CreatedAt <= endDateTime)
            .Where(wh => wh.ToStageKey == NoticeStatus.Closed)
            .CountAsync(ct);

        // Calculate average response time (time from assignment to first action)
        var avgResponseTimeHours = await CalculateAverageResponseTimeAsync(userId, orgId, range, ct);

        // Calculate SLA adherence
        var slaData = await CalculateSlaAdherenceAsync(userId, orgId, range, ct);

        // Get workflow transitions performed
        var workflowTransitions = await _context.WorkflowHistories
            .Where(wh => wh.Notice.OrganizationId == orgId)
            .Where(wh => wh.PerformedById == userId)
            .Where(wh => wh.CreatedAt >= startDateTime && wh.CreatedAt <= endDateTime)
            .Where(wh => wh.EventType == WorkflowHistoryEventTypes.StageTransition)
            .CountAsync(ct);

        // Get comments added
        var commentsAdded = await _context.Comments
            .Where(c => c.UserId == userId)
            .Where(c => c.CreatedAt >= startDateTime && c.CreatedAt <= endDateTime)
            .Where(c => c.Notice.OrganizationId == orgId)
            .CountAsync(ct);

        // Get documents reviewed
        var documentsReviewed = await _context.DocumentRequests
            .Where(dr => dr.ReviewedById == userId)
            .Where(dr => dr.UpdatedAt >= startDateTime && dr.UpdatedAt <= endDateTime)
            .Where(dr => dr.Notice.OrganizationId == orgId)
            .CountAsync(ct);

        // Get trend comparison
        var trend = await GetUserPerformanceTrendAsync(userId, orgId, range, ct);

        return new UserPerformanceSummary(
            UserId: user.Id,
            UserName: user.Name ?? user.Email ?? "Unknown",
            UserEmail: user.Email,
            AvatarUrl: user.AvatarUrl,
            TasksCompleted: tasksCompleted,
            TasksAssigned: tasksAssigned,
            TasksOverdue: tasksOverdue,
            NoticesHandled: noticesHandled,
            NoticesClosed: noticesClosed,
            AverageResponseTimeHours: Math.Round(avgResponseTimeHours, 2),
            SlaAdherencePercent: Math.Round(slaData.adherencePercent, 2),
            WorkflowTransitionsPerformed: workflowTransitions,
            CommentsAdded: commentsAdded,
            DocumentsReviewed: documentsReviewed,
            Trend: trend
        );
    }

    public async Task<List<UserPerformanceSummary>> GetTeamPerformanceAsync(
        Guid orgId,
        DateRange range,
        CancellationToken ct)
    {
        // Get all active members of the organization
        var members = await _context.OrganizationMembers
            .Where(m => m.OrganizationId == orgId)
            .Where(m => m.Status == "active")
            .Select(m => m.UserId)
            .ToListAsync(ct);

        var performances = new List<UserPerformanceSummary>();

        foreach (var memberId in members)
        {
            try
            {
                var performance = await GetUserPerformanceAsync(memberId, orgId, range, ct);
                performances.Add(performance);
            }
            catch (KeyNotFoundException)
            {
                // Skip users that no longer exist
                _logger.LogWarning("User {UserId} not found while getting team performance", memberId);
            }
        }

        return performances.OrderByDescending(p => p.TasksCompleted).ToList();
    }

    public async Task<List<UserRanking>> GetLeaderboardAsync(
        Guid orgId,
        string metric,
        DateRange range,
        int limit,
        CancellationToken ct)
    {
        var normalizedMetric = metric.ToLowerInvariant().Replace("-", "_").Replace(" ", "_");

        if (!ValidMetrics.Contains(normalizedMetric))
        {
            throw new ArgumentException(
                $"Invalid metric. Valid options: {string.Join(", ", ValidMetrics)}",
                nameof(metric));
        }

        // Get all team performances
        var performances = await GetTeamPerformanceAsync(orgId, range, ct);

        // Get previous period for rank comparison
        var periodDays = range.EndDate.DayNumber - range.StartDate.DayNumber + 1;
        var prevRange = new DateRange(
            range.StartDate.AddDays(-periodDays),
            range.StartDate.AddDays(-1)
        );

        var previousPerformances = await GetTeamPerformanceAsync(orgId, prevRange, ct);

        // Rank users by the specified metric
        var rankedPerformances = performances
            .Select(p => (
                Performance: p,
                Value: GetMetricValue(p, normalizedMetric)
            ))
            .OrderByDescending(x => x.Value)
            .Take(limit)
            .ToList();

        // Build previous rankings lookup
        var previousRankings = previousPerformances
            .Select((p, idx) => (UserId: p.UserId, Value: GetMetricValue(p, normalizedMetric), Rank: idx + 1))
            .OrderByDescending(x => x.Value)
            .Select((x, idx) => (x.UserId, Rank: idx + 1))
            .ToDictionary(x => x.UserId, x => x.Rank);

        var rankings = rankedPerformances
            .Select((x, index) =>
            {
                var currentRank = index + 1;
                var previousRank = previousRankings.TryGetValue(x.Performance.UserId, out var prevRank) ? prevRank : (int?)null;
                var rankChange = previousRank.HasValue ? previousRank.Value - currentRank : (int?)null;

                return new UserRanking(
                    Rank: currentRank,
                    UserId: x.Performance.UserId,
                    UserName: x.Performance.UserName,
                    AvatarUrl: x.Performance.AvatarUrl,
                    MetricValue: x.Value,
                    MetricName: GetMetricDisplayName(normalizedMetric),
                    PreviousRank: previousRank,
                    RankChange: rankChange
                );
            })
            .ToList();

        return rankings;
    }

    private async Task<decimal> CalculateAverageResponseTimeAsync(
        Guid userId,
        Guid orgId,
        DateRange range,
        CancellationToken ct)
    {
        var startDateTime = ToUtcDateTime(range.StartDate, TimeOnly.MinValue);
        var endDateTime = ToUtcDateTime(range.EndDate, TimeOnly.MaxValue);

        // Get notices assigned to user with their first action time
        var notices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.AssignedToId == userId)
            .Where(n => n.AssignedAt.HasValue && n.AssignedAt >= startDateTime && n.AssignedAt <= endDateTime)
            .Select(n => new
            {
                n.Id,
                n.AssignedAt,
                FirstAction = n.Comments
                    .Where(c => c.UserId == userId)
                    .OrderBy(c => c.CreatedAt)
                    .Select(c => (DateTime?)c.CreatedAt)
                    .FirstOrDefault()
                ?? n.Tasks
                    .Where(t => t.CreatedById == userId)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => (DateTime?)t.CreatedAt)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var responseTimes = notices
            .Where(n => n.AssignedAt.HasValue && n.FirstAction.HasValue)
            .Select(n => (n.FirstAction!.Value - n.AssignedAt!.Value).TotalHours)
            .ToList();

        return responseTimes.Count > 0 ? (decimal)responseTimes.Average() : 0;
    }

    private async Task<(decimal adherencePercent, int totalWithSla)> CalculateSlaAdherenceAsync(
        Guid userId,
        Guid orgId,
        DateRange range,
        CancellationToken ct)
    {
        var startDateTime = ToUtcDateTime(range.StartDate, TimeOnly.MinValue);
        var endDateTime = ToUtcDateTime(range.EndDate, TimeOnly.MaxValue);

        // Get tasks with due dates that the user completed
        var tasks = await _context.Tasks
            .Where(t => t.CompletedById == userId)
            .Where(t => t.CompletedAt.HasValue && t.CompletedAt >= startDateTime && t.CompletedAt <= endDateTime)
            .Where(t => t.DueDate.HasValue)
            .Where(t => t.Notice.OrganizationId == orgId)
            .Select(t => new
            {
                t.DueDate,
                t.CompletedAt
            })
            .ToListAsync(ct);

        if (tasks.Count == 0)
            return (100m, 0);

        var tasksOnTime = tasks.Count(t => t.CompletedAt <= t.DueDate);
        var adherencePercent = (decimal)tasksOnTime / tasks.Count * 100;

        return (adherencePercent, tasks.Count);
    }

    private async Task<UserPerformanceTrend?> GetUserPerformanceTrendAsync(
        Guid userId,
        Guid orgId,
        DateRange range,
        CancellationToken ct)
    {
        // Calculate previous period
        var periodDays = range.EndDate.DayNumber - range.StartDate.DayNumber + 1;
        var prevRange = new DateRange(
            range.StartDate.AddDays(-periodDays),
            range.StartDate.AddDays(-1)
        );

        var prevStartDateTime = ToUtcDateTime(prevRange.StartDate, TimeOnly.MinValue);
        var prevEndDateTime = ToUtcDateTime(prevRange.EndDate, TimeOnly.MaxValue);

        var currentStartDateTime = ToUtcDateTime(range.StartDate, TimeOnly.MinValue);
        var currentEndDateTime = ToUtcDateTime(range.EndDate, TimeOnly.MaxValue);

        // Get previous period metrics
        var prevTasksCompleted = await _context.Tasks
            .Where(t => t.CompletedById == userId)
            .Where(t => t.CompletedAt.HasValue && t.CompletedAt >= prevStartDateTime && t.CompletedAt <= prevEndDateTime)
            .Where(t => t.Notice.OrganizationId == orgId)
            .CountAsync(ct);

        var currentTasksCompleted = await _context.Tasks
            .Where(t => t.CompletedById == userId)
            .Where(t => t.CompletedAt.HasValue && t.CompletedAt >= currentStartDateTime && t.CompletedAt <= currentEndDateTime)
            .Where(t => t.Notice.OrganizationId == orgId)
            .CountAsync(ct);

        var prevNoticesHandled = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.AssignedToId == userId)
            .Where(n => n.AssignedAt.HasValue && n.AssignedAt >= prevStartDateTime && n.AssignedAt <= prevEndDateTime)
            .CountAsync(ct);

        var currentNoticesHandled = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.AssignedToId == userId)
            .Where(n => n.AssignedAt.HasValue && n.AssignedAt >= currentStartDateTime && n.AssignedAt <= currentEndDateTime)
            .CountAsync(ct);

        var prevSla = await CalculateSlaAdherenceAsync(userId, orgId, prevRange, ct);
        var currentSla = await CalculateSlaAdherenceAsync(userId, orgId, range, ct);

        return new UserPerformanceTrend(
            TasksCompletedChange: currentTasksCompleted - prevTasksCompleted,
            TasksCompletedChangePercent: prevTasksCompleted > 0
                ? Math.Round((decimal)(currentTasksCompleted - prevTasksCompleted) / prevTasksCompleted * 100, 2)
                : 0,
            NoticesHandledChange: currentNoticesHandled - prevNoticesHandled,
            NoticesHandledChangePercent: prevNoticesHandled > 0
                ? Math.Round((decimal)(currentNoticesHandled - prevNoticesHandled) / prevNoticesHandled * 100, 2)
                : 0,
            SlaAdherenceChange: Math.Round(currentSla.adherencePercent - prevSla.adherencePercent, 2)
        );
    }

    private static decimal GetMetricValue(UserPerformanceSummary performance, string metric)
    {
        return metric switch
        {
            "tasks_completed" => performance.TasksCompleted,
            "notices_handled" => performance.NoticesHandled,
            "notices_closed" => performance.NoticesClosed,
            "sla_adherence" => performance.SlaAdherencePercent,
            "response_time" => -performance.AverageResponseTimeHours, // Negative so lower is better
            "workflow_transitions" => performance.WorkflowTransitionsPerformed,
            "comments" => performance.CommentsAdded,
            _ => performance.TasksCompleted
        };
    }

    private static string GetMetricDisplayName(string metric)
    {
        return metric switch
        {
            "tasks_completed" => "Tasks Completed",
            "notices_handled" => "Notices Handled",
            "notices_closed" => "Notices Closed",
            "sla_adherence" => "SLA Adherence %",
            "response_time" => "Avg Response Time (hrs)",
            "workflow_transitions" => "Workflow Transitions",
            "comments" => "Comments Added",
            _ => metric
        };
    }
}
