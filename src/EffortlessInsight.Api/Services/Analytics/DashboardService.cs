using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Analytics;

/// <summary>
/// Interface for dashboard analytics service.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Get comprehensive dashboard metrics for an organization.
    /// </summary>
    Task<DashboardMetrics> GetDashboardMetricsAsync(Guid orgId, CancellationToken ct = default);
}

/// <summary>
/// Dashboard metrics aggregate containing all dashboard data.
/// </summary>
public record DashboardMetrics
{
    public NoticeMetrics Notices { get; init; } = new();
    public TaskMetrics Tasks { get; init; } = new();
    public WorkflowMetrics Workflows { get; init; } = new();
    public UpcomingDeadlines Deadlines { get; init; } = new();
    public RecentActivity Activity { get; init; } = new();
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Notice-related metrics.
/// </summary>
public record NoticeMetrics
{
    public int Total { get; init; }
    public int Open { get; init; }
    public int Closed { get; init; }
    public int Overdue { get; init; }
    public int Processing { get; init; }
    public int InProgress { get; init; }
    public int UploadedThisWeek { get; init; }
    public int ClosedThisWeek { get; init; }
    public decimal TotalDemandAmount { get; init; }
    public Dictionary<string, int> ByType { get; init; } = new();
    public Dictionary<string, int> ByPriority { get; init; } = new();
}

/// <summary>
/// Task-related metrics.
/// </summary>
public record TaskMetrics
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int InProgress { get; init; }
    public int Completed { get; init; }
    public int CompletedThisWeek { get; init; }
    public int Overdue { get; init; }
    public int Blocked { get; init; }
    public int OnHold { get; init; }
    public decimal AvgCompletionTimeHours { get; init; }
    public Dictionary<string, int> ByPriority { get; init; } = new();
}

/// <summary>
/// Workflow-related metrics.
/// </summary>
public record WorkflowMetrics
{
    public int ActiveWorkflows { get; init; }
    public int CompletedWorkflows { get; init; }
    public int CompletedThisWeek { get; init; }
    public decimal AvgCompletionTimeHours { get; init; }
    public int SlaBreaches { get; init; }
    public int AtRiskWorkflows { get; init; }
    public Dictionary<string, int> ByStatus { get; init; } = new();
}

/// <summary>
/// Upcoming deadline information.
/// </summary>
public record UpcomingDeadlines
{
    public List<DeadlineItem> Next7Days { get; init; } = new();
    public int TotalUpcoming { get; init; }
    public int OverdueCount { get; init; }
    public int DueToday { get; init; }
    public int DueTomorrow { get; init; }
    public int DueThisWeek { get; init; }
}

/// <summary>
/// Individual deadline item.
/// </summary>
public record DeadlineItem
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty; // "notice" or "task"
    public string Title { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }
    public int DaysRemaining { get; init; }
    public bool IsOverdue { get; init; }
    public string Priority { get; init; } = string.Empty;
    public Guid? NoticeId { get; init; }
    public string? NoticeNumber { get; init; }
}

/// <summary>
/// Recent activity information.
/// </summary>
public record RecentActivity
{
    public List<ActivityItem> Items { get; init; } = new();
    public int TotalToday { get; init; }
    public int TotalThisWeek { get; init; }
}

/// <summary>
/// Individual activity item.
/// </summary>
public record ActivityItem
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public ActivityActorInfo? Actor { get; init; }
    public Guid? NoticeId { get; init; }
    public string? NoticeNumber { get; init; }
}

/// <summary>
/// Activity actor information.
/// </summary>
public record ActivityActorInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
}

/// <summary>
/// Implementation of dashboard analytics service.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        ApplicationDbContext dbContext,
        ILogger<DashboardService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DashboardMetrics> GetDashboardMetricsAsync(Guid orgId, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating dashboard metrics for organization {OrgId}", orgId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        // Run queries in parallel for better performance
        var noticeMetricsTask = GetNoticeMetricsAsync(orgId, today, weekAgo, ct);
        var taskMetricsTask = GetTaskMetricsAsync(orgId, today, weekAgo, ct);
        var workflowMetricsTask = GetWorkflowMetricsAsync(orgId, weekAgo, ct);
        var deadlinesTask = GetUpcomingDeadlinesAsync(orgId, today, ct);
        var activityTask = GetRecentActivityAsync(orgId, weekAgo, ct);

        await Task.WhenAll(noticeMetricsTask, taskMetricsTask, workflowMetricsTask, deadlinesTask, activityTask);

        return new DashboardMetrics
        {
            Notices = await noticeMetricsTask,
            Tasks = await taskMetricsTask,
            Workflows = await workflowMetricsTask,
            Deadlines = await deadlinesTask,
            Activity = await activityTask,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<NoticeMetrics> GetNoticeMetricsAsync(
        Guid orgId,
        DateOnly today,
        DateTime weekAgo,
        CancellationToken ct)
    {
        var notices = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId && !n.IsDeleted)
            .Select(n => new
            {
                n.Status,
                n.Priority,
                n.NoticeType,
                n.ResponseDeadline,
                n.TotalDemand,
                n.CreatedAt
            })
            .ToListAsync(ct);

        var closedStatuses = new[] { NoticeStatus.Closed, NoticeStatus.Archived };
        var openStatuses = new[] { NoticeStatus.Uploaded, NoticeStatus.Processing, NoticeStatus.Analyzed, NoticeStatus.InProgress, NoticeStatus.Responded };

        var overdueNotices = notices.Count(n =>
            openStatuses.Contains(n.Status) &&
            n.ResponseDeadline.HasValue &&
            n.ResponseDeadline.Value < today);

        var byType = notices
            .Where(n => !string.IsNullOrEmpty(n.NoticeType))
            .GroupBy(n => n.NoticeType!)
            .ToDictionary(g => g.Key, g => g.Count());

        var byPriority = notices
            .GroupBy(n => n.Priority)
            .ToDictionary(g => g.Key, g => g.Count());

        return new NoticeMetrics
        {
            Total = notices.Count,
            Open = notices.Count(n => openStatuses.Contains(n.Status)),
            Closed = notices.Count(n => closedStatuses.Contains(n.Status)),
            Overdue = overdueNotices,
            Processing = notices.Count(n => n.Status == NoticeStatus.Processing),
            InProgress = notices.Count(n => n.Status == NoticeStatus.InProgress),
            UploadedThisWeek = notices.Count(n => n.CreatedAt >= weekAgo),
            ClosedThisWeek = notices.Count(n =>
                closedStatuses.Contains(n.Status) && n.CreatedAt >= weekAgo),
            TotalDemandAmount = notices.Sum(n => n.TotalDemand ?? 0),
            ByType = byType,
            ByPriority = byPriority
        };
    }

    private async Task<TaskMetrics> GetTaskMetricsAsync(
        Guid orgId,
        DateOnly today,
        DateTime weekAgo,
        CancellationToken ct)
    {
        var tasks = await _dbContext.Tasks
            .Where(t => t.Notice.OrganizationId == orgId && !t.IsDeleted)
            .Select(t => new
            {
                t.Status,
                t.Priority,
                t.DueDate,
                t.CreatedAt,
                t.CompletedAt
            })
            .ToListAsync(ct);

        var completedTasks = tasks.Where(t =>
            t.Status == TaskStatusValues.Done && t.CompletedAt.HasValue).ToList();

        var avgCompletionTime = completedTasks.Any()
            ? completedTasks.Average(t => (t.CompletedAt!.Value - t.CreatedAt).TotalHours)
            : 0;

        var todoDate = DateTime.UtcNow.Date;
        var overdueTasks = tasks.Count(t =>
            TaskStatusValues.ActiveStatuses.Contains(t.Status) &&
            t.DueDate.HasValue &&
            t.DueDate.Value < todoDate);

        var byPriority = tasks
            .GroupBy(t => t.Priority)
            .ToDictionary(g => g.Key, g => g.Count());

        return new TaskMetrics
        {
            Total = tasks.Count,
            Pending = tasks.Count(t => t.Status == TaskStatusValues.Todo),
            InProgress = tasks.Count(t => t.Status == TaskStatusValues.InProgress),
            Completed = tasks.Count(t => t.Status == TaskStatusValues.Done),
            CompletedThisWeek = tasks.Count(t =>
                t.Status == TaskStatusValues.Done &&
                t.CompletedAt.HasValue &&
                t.CompletedAt.Value >= weekAgo),
            Overdue = overdueTasks,
            Blocked = tasks.Count(t => t.Status == TaskStatusValues.Blocked),
            OnHold = tasks.Count(t => t.Status == TaskStatusValues.OnHold),
            AvgCompletionTimeHours = (decimal)avgCompletionTime,
            ByPriority = byPriority
        };
    }

    private async Task<WorkflowMetrics> GetWorkflowMetricsAsync(
        Guid orgId,
        DateTime weekAgo,
        CancellationToken ct)
    {
        var workflows = await _dbContext.NoticeWorkflowInstances
            .Where(w => w.Notice.OrganizationId == orgId)
            .Select(w => new
            {
                w.Status,
                w.SlaStatus,
                w.CompletedAt,
                w.CreatedAt,
                w.TotalTimeMinutes
            })
            .ToListAsync(ct);

        var completedWorkflows = workflows
            .Where(w => w.Status == WorkflowInstanceStatuses.Completed)
            .ToList();

        var avgCompletionTime = completedWorkflows.Any()
            ? completedWorkflows.Average(w => w.TotalTimeMinutes / 60.0)
            : 0;

        var byStatus = workflows
            .GroupBy(w => w.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        return new WorkflowMetrics
        {
            ActiveWorkflows = workflows.Count(w => w.Status == WorkflowInstanceStatuses.Active),
            CompletedWorkflows = completedWorkflows.Count,
            CompletedThisWeek = completedWorkflows.Count(w =>
                w.CompletedAt.HasValue && w.CompletedAt.Value >= weekAgo),
            AvgCompletionTimeHours = (decimal)avgCompletionTime,
            SlaBreaches = workflows.Count(w => w.SlaStatus == WorkflowSlaStatuses.Breached),
            AtRiskWorkflows = workflows.Count(w =>
                w.Status == WorkflowInstanceStatuses.Active &&
                (w.SlaStatus == WorkflowSlaStatuses.AtRisk || w.SlaStatus == WorkflowSlaStatuses.Warning)),
            ByStatus = byStatus
        };
    }

    private async Task<UpcomingDeadlines> GetUpcomingDeadlinesAsync(
        Guid orgId,
        DateOnly today,
        CancellationToken ct)
    {
        var next7Days = today.AddDays(7);
        var tomorrow = today.AddDays(1);
        var thisWeekEnd = today.AddDays(7);

        // Get notice deadlines
        var noticeDeadlines = await _dbContext.Notices
            .Where(n => n.OrganizationId == orgId &&
                !n.IsDeleted &&
                n.ResponseDeadline.HasValue &&
                n.ResponseDeadline.Value <= next7Days &&
                n.Status != NoticeStatus.Closed &&
                n.Status != NoticeStatus.Archived)
            .Select(n => new DeadlineItem
            {
                Id = n.Id,
                Type = "notice",
                Title = n.NoticeNumber ?? "Notice #" + n.Id.ToString().Substring(0, 8),
                DueDate = n.ResponseDeadline!.Value,
                DaysRemaining = n.ResponseDeadline.Value.DayNumber - today.DayNumber,
                IsOverdue = n.ResponseDeadline.Value < today,
                Priority = n.Priority,
                NoticeId = n.Id,
                NoticeNumber = n.NoticeNumber
            })
            .ToListAsync(ct);

        // Get task deadlines
        var todayDateTime = DateTime.UtcNow.Date;
        var next7DaysDateTime = todayDateTime.AddDays(7);

        var taskDeadlines = await _dbContext.Tasks
            .Where(t => t.Notice.OrganizationId == orgId &&
                !t.IsDeleted &&
                t.DueDate.HasValue &&
                t.DueDate.Value <= next7DaysDateTime &&
                TaskStatusValues.ActiveStatuses.Contains(t.Status))
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.DueDate,
                t.Priority,
                t.NoticeId,
                NoticeNumber = t.Notice.NoticeNumber
            })
            .ToListAsync(ct);

        var taskDeadlineItems = taskDeadlines.Select(t => new DeadlineItem
        {
            Id = t.Id,
            Type = "task",
            Title = t.Title,
            DueDate = DateOnly.FromDateTime(t.DueDate!.Value),
            DaysRemaining = (int)(t.DueDate!.Value.Date - todayDateTime).TotalDays,
            IsOverdue = t.DueDate.Value < todayDateTime,
            Priority = t.Priority,
            NoticeId = t.NoticeId,
            NoticeNumber = t.NoticeNumber
        }).ToList();

        var allDeadlines = noticeDeadlines
            .Concat(taskDeadlineItems)
            .OrderBy(d => d.DueDate)
            .Take(20)
            .ToList();

        return new UpcomingDeadlines
        {
            Next7Days = allDeadlines,
            TotalUpcoming = allDeadlines.Count,
            OverdueCount = allDeadlines.Count(d => d.IsOverdue),
            DueToday = allDeadlines.Count(d => d.DueDate == today),
            DueTomorrow = allDeadlines.Count(d => d.DueDate == tomorrow),
            DueThisWeek = allDeadlines.Count(d => d.DueDate <= thisWeekEnd && !d.IsOverdue)
        };
    }

    private async Task<RecentActivity> GetRecentActivityAsync(
        Guid orgId,
        DateTime weekAgo,
        CancellationToken ct)
    {
        var todayStart = DateTime.UtcNow.Date;

        var activities = await _dbContext.ActivityLogs
            .Where(a => a.OrganizationId == orgId && a.CreatedAt >= weekAgo)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .Select(a => new
            {
                a.Id,
                a.ActivityType,
                a.Message,
                a.CreatedAt,
                a.ActorId,
                ActorName = a.Actor != null ? a.Actor.FullName : null,
                ActorAvatar = a.Actor != null ? a.Actor.AvatarUrl : null,
                a.NoticeId,
                NoticeNumber = a.Notice != null ? a.Notice.NoticeNumber : null
            })
            .ToListAsync(ct);

        var activityItems = activities.Select(a => new ActivityItem
        {
            Id = a.Id,
            Type = a.ActivityType,
            Message = a.Message,
            Timestamp = a.CreatedAt,
            Actor = a.ActorId.HasValue ? new ActivityActorInfo
            {
                Id = a.ActorId.Value,
                Name = a.ActorName ?? "Unknown",
                AvatarUrl = a.ActorAvatar
            } : null,
            NoticeId = a.NoticeId,
            NoticeNumber = a.NoticeNumber
        }).ToList();

        var totalToday = await _dbContext.ActivityLogs
            .CountAsync(a => a.OrganizationId == orgId && a.CreatedAt >= todayStart, ct);

        var totalThisWeek = await _dbContext.ActivityLogs
            .CountAsync(a => a.OrganizationId == orgId && a.CreatedAt >= weekAgo, ct);

        return new RecentActivity
        {
            Items = activityItems,
            TotalToday = totalToday,
            TotalThisWeek = totalThisWeek
        };
    }
}
