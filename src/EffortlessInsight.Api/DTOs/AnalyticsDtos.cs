namespace EffortlessInsight.Api.DTOs;

// =============================================================================
// COMMON ANALYTICS TYPES
// =============================================================================

/// <summary>
/// Represents a date range for analytics queries.
/// </summary>
public record DateRange(DateOnly StartDate, DateOnly EndDate)
{
    public static DateRange Last7Days() =>
        new(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), DateOnly.FromDateTime(DateTime.UtcNow));

    public static DateRange Last30Days() =>
        new(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow));

    public static DateRange Last90Days() =>
        new(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)), DateOnly.FromDateTime(DateTime.UtcNow));

    public static DateRange ThisMonth()
    {
        var now = DateTime.UtcNow;
        return new(new DateOnly(now.Year, now.Month, 1), DateOnly.FromDateTime(now));
    }

    public static DateRange LastMonth()
    {
        var now = DateTime.UtcNow;
        var lastMonth = now.AddMonths(-1);
        return new(
            new DateOnly(lastMonth.Year, lastMonth.Month, 1),
            new DateOnly(now.Year, now.Month, 1).AddDays(-1));
    }
}

/// <summary>
/// A single data point in a time series.
/// </summary>
public record TimeSeriesDataPoint(
    DateOnly Date,
    decimal Value,
    string? Label = null
);

// =============================================================================
// GAP-RPT-001: NOTICE ANALYTICS DTOs
// =============================================================================

/// <summary>
/// Summary statistics for notices.
/// </summary>
public record NoticeAnalyticsSummary(
    int TotalNotices,
    StatusBreakdown ByStatus,
    PriorityBreakdown ByPriority,
    decimal AverageResolutionTimeHours,
    int OverdueCount,
    int DueTodayCount,
    int DueThisWeekCount,
    decimal TotalDemandAmount,
    ComparisonMetrics? Comparison
);

/// <summary>
/// Notice counts by status.
/// </summary>
public record StatusBreakdown(
    int Uploaded,
    int Processing,
    int Analyzed,
    int InProgress,
    int Responded,
    int Closed,
    int Archived,
    int Failed
);

/// <summary>
/// Notice counts by priority.
/// </summary>
public record PriorityBreakdown(
    int Low,
    int Medium,
    int High,
    int Critical
);

/// <summary>
/// Comparison with previous period.
/// </summary>
public record ComparisonMetrics(
    int NewNoticesChange,
    decimal NewNoticesChangePercent,
    int ClosedNoticesChange,
    decimal ClosedNoticesChangePercent,
    decimal AvgResolutionTimeChange,
    decimal AvgResolutionTimeChangePercent
);

/// <summary>
/// Notice breakdown by category.
/// </summary>
public record CategoryBreakdown(
    string Category,
    int Count,
    decimal Percentage,
    decimal TotalDemand,
    decimal AverageResolutionHours
);

/// <summary>
/// Resolution metrics for notices.
/// </summary>
public record ResolutionMetrics(
    decimal AverageTimeToCloseHours,
    decimal MedianTimeToCloseHours,
    decimal MinTimeToCloseHours,
    decimal MaxTimeToCloseHours,
    int TotalClosed,
    int TotalOpen,
    decimal SlaComplianceRate,
    int SlaMetCount,
    int SlaBreachedCount,
    List<ResolutionByPriority> ByPriority,
    List<ResolutionByCategory> ByCategory
);

/// <summary>
/// Resolution time by priority level.
/// </summary>
public record ResolutionByPriority(
    string Priority,
    decimal AverageHours,
    int Count,
    decimal SlaComplianceRate
);

/// <summary>
/// Resolution time by notice category.
/// </summary>
public record ResolutionByCategory(
    string Category,
    decimal AverageHours,
    int Count
);

// =============================================================================
// GAP-RPT-002: USER PERFORMANCE DTOs
// =============================================================================

/// <summary>
/// Performance summary for a single user.
/// </summary>
public record UserPerformanceSummary(
    Guid UserId,
    string UserName,
    string? UserEmail,
    string? AvatarUrl,
    int TasksCompleted,
    int TasksAssigned,
    int TasksOverdue,
    int NoticesHandled,
    int NoticesClosed,
    decimal AverageResponseTimeHours,
    decimal SlaAdherencePercent,
    int WorkflowTransitionsPerformed,
    int CommentsAdded,
    int DocumentsReviewed,
    UserPerformanceTrend? Trend
);

/// <summary>
/// Trend data for user performance.
/// </summary>
public record UserPerformanceTrend(
    int TasksCompletedChange,
    decimal TasksCompletedChangePercent,
    int NoticesHandledChange,
    decimal NoticesHandledChangePercent,
    decimal SlaAdherenceChange
);

/// <summary>
/// User ranking entry for leaderboard.
/// </summary>
public record UserRanking(
    int Rank,
    Guid UserId,
    string UserName,
    string? AvatarUrl,
    decimal MetricValue,
    string MetricName,
    int? PreviousRank,
    int? RankChange
);

/// <summary>
/// Team performance aggregated metrics.
/// </summary>
public record TeamPerformanceMetrics(
    Guid OrganizationId,
    int TotalMembers,
    int ActiveMembers,
    decimal TeamSlaAdherencePercent,
    decimal AverageTasksPerMember,
    decimal AverageNoticesPerMember,
    UserPerformanceSummary TopPerformer,
    List<UserPerformanceSummary> MemberPerformance
);

// =============================================================================
// ANALYTICS API REQUEST/RESPONSE DTOs
// =============================================================================

/// <summary>
/// Request parameters for analytics endpoints.
/// </summary>
public record AnalyticsRequest(
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Interval = "daily" // daily, weekly, monthly
);

/// <summary>
/// Response for notice summary endpoint.
/// </summary>
public record NoticeSummaryResponse(
    NoticeAnalyticsSummary Summary,
    DateRange Period
);

/// <summary>
/// Response for notice trends endpoint.
/// </summary>
public record NoticeTrendResponse(
    string Metric,
    string Interval,
    List<TimeSeriesDataPoint> DataPoints,
    DateRange Period
);

/// <summary>
/// Response for category breakdown endpoint.
/// </summary>
public record CategoryBreakdownResponse(
    List<CategoryBreakdown> Categories,
    int TotalNotices,
    DateRange Period
);

/// <summary>
/// Response for resolution metrics endpoint.
/// </summary>
public record ResolutionMetricsResponse(
    ResolutionMetrics Metrics,
    DateRange Period
);

/// <summary>
/// Response for user performance endpoint.
/// </summary>
public record UserPerformanceResponse(
    UserPerformanceSummary Performance,
    DateRange Period
);

/// <summary>
/// Response for team performance endpoint.
/// </summary>
public record TeamPerformanceResponse(
    List<UserPerformanceSummary> Members,
    int TotalMembers,
    decimal TeamAverageSla,
    DateRange Period
);

/// <summary>
/// Response for leaderboard endpoint.
/// </summary>
public record LeaderboardResponse(
    string Metric,
    List<UserRanking> Rankings,
    int TotalParticipants,
    DateRange Period
);
