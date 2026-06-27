namespace EffortlessInsight.Api.DTOs;

// =============================================================================
// Chart-Ready Dashboard DTOs
// Optimized for frontend charting libraries (Chart.js, Recharts, etc.)
// =============================================================================

/// <summary>
/// Generic chart data point with label, value, and optional color
/// </summary>
public record ChartDataPoint(
    string Label,
    decimal Value,
    string? Color = null
);

// Note: TimeSeriesDataPoint is defined in AnalyticsDtos.cs

/// <summary>
/// Pie/Doughnut chart data format
/// </summary>
public record PieChartData(
    List<string> Labels,
    List<decimal> Data,
    List<string> Colors,
    decimal Total
);

/// <summary>
/// Bar chart data format with optional multiple datasets
/// </summary>
public record BarChartData(
    List<string> Labels,
    List<BarChartDataset> Datasets
);

public record BarChartDataset(
    string Label,
    List<decimal> Data,
    string? Color = null,
    string? BackgroundColor = null
);

/// <summary>
/// Line chart data format for time series
/// </summary>
public record LineChartData(
    List<string> Labels,
    List<LineChartDataset> Datasets
);

public record LineChartDataset(
    string Label,
    List<decimal> Data,
    string? BorderColor = null,
    string? BackgroundColor = null,
    bool? Fill = null
);

// =============================================================================
// Dashboard Response DTOs
// =============================================================================

/// <summary>
/// Notice counts by status for pie/doughnut chart
/// </summary>
public record NoticesByStatusResponse(
    PieChartData ChartData,
    List<NoticeStatusCount> Details
);

public record NoticeStatusCount(
    string Status,
    int Count,
    decimal Percentage,
    string Color
);

/// <summary>
/// Notice counts by type for bar chart
/// </summary>
public record NoticesByTypeResponse(
    BarChartData ChartData,
    List<NoticeTypeCount> Details
);

public record NoticeTypeCount(
    string Type,
    string TypeLabel,
    int Count,
    decimal Percentage
);

/// <summary>
/// Notice creation timeline for line chart
/// </summary>
public record NoticeTimelineResponse(
    LineChartData ChartData,
    string Period,
    int TotalNotices,
    decimal AveragePerPeriod
);

/// <summary>
/// Task summary with completion rates
/// </summary>
public record TaskSummaryResponse(
    int TotalTasks,
    int CompletedTasks,
    int OverdueTasks,
    int InProgressTasks,
    decimal CompletionRate,
    PieChartData StatusChart,
    PieChartData PriorityChart
);

/// <summary>
/// Team activity metrics
/// </summary>
public record TeamActivityResponse(
    BarChartData ActivityByMember,
    LineChartData ActivityOverTime,
    List<TeamMemberActivity> TopContributors
);

public record TeamMemberActivity(
    Guid UserId,
    string UserName,
    string? AvatarUrl,
    int TasksCompleted,
    int CommentsAdded,
    int DocumentsUploaded,
    decimal ActivityScore
);

/// <summary>
/// Workflow stage distribution
/// </summary>
public record WorkflowStageDistributionResponse(
    PieChartData ChartData,
    List<WorkflowStageCount> Details,
    int TotalActiveWorkflows
);

public record WorkflowStageCount(
    string StageKey,
    string StageName,
    int Count,
    decimal Percentage,
    string Color
);

/// <summary>
/// Dashboard overview combining key metrics
/// </summary>
public record DashboardOverviewResponse(
    DashboardOverviewMetrics Metrics,
    NoticesByStatusResponse NoticesByStatus,
    TaskSummaryResponse TaskSummary,
    List<RecentActivityItem> RecentActivity
);

public record DashboardOverviewMetrics(
    int TotalNotices,
    int ActiveNotices,
    int PendingTasks,
    int OverdueTasks,
    int TeamMembers,
    decimal NoticeResolutionRate,
    decimal TaskCompletionRate,
    int NoticesThisMonth,
    int NoticesLastMonth,
    decimal NoticeGrowthPercent
);

public record RecentActivityItem(
    DateTime Timestamp,
    string ActivityType,
    string Message,
    Guid? ActorId,
    string? ActorName,
    string? ActorAvatarUrl,
    Guid? EntityId,
    string? EntityType
);

// =============================================================================
// Request DTOs
// =============================================================================

public record DashboardTimeRange(
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string? Period = null  // "7d", "30d", "90d", "1y", "all"
);
