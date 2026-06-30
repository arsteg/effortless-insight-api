using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Analytics;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Analytics and dashboard metrics endpoints.
/// Includes GAP-RPT-001 (Notice Analytics) and GAP-RPT-002 (User Performance Metrics).
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly INoticeAnalyticsService _noticeAnalyticsService;
    private readonly IUserPerformanceService _userPerformanceService;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IDashboardService dashboardService,
        INoticeAnalyticsService noticeAnalyticsService,
        IUserPerformanceService userPerformanceService,
        ICurrentOrganizationService orgService,
        ILogger<AnalyticsController> logger)
    {
        _dashboardService = dashboardService;
        _noticeAnalyticsService = noticeAnalyticsService;
        _userPerformanceService = userPerformanceService;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);

    private Guid GetOrganizationId() =>
        _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");

    private DateRange GetDateRange(DateOnly? startDate, DateOnly? endDate)
    {
        // Default to last 30 days if not specified
        var end = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = startDate ?? end.AddDays(-30);

        // Validate range
        if (start > end)
            throw new ArgumentException("Start date cannot be after end date");

        // Limit to 1 year max
        if ((end.DayNumber - start.DayNumber) > 365)
            throw new ArgumentException("Date range cannot exceed 1 year");

        return new DateRange(start, end);
    }

    /// <summary>
    /// Get dashboard metrics for the current organization.
    /// Returns comprehensive analytics including notice counts, task stats,
    /// workflow metrics, upcoming deadlines, and recent activity.
    /// </summary>
    /// <remarks>
    /// This endpoint aggregates data from multiple sources to provide
    /// a complete dashboard overview:
    /// - **Notices**: Total, open, closed, overdue counts with breakdown by type/priority
    /// - **Tasks**: Pending, in progress, completed with average completion time
    /// - **Workflows**: Active workflows, SLA metrics, completion rates
    /// - **Deadlines**: Next 7 days deadlines for both notices and tasks
    /// - **Activity**: Last 10 recent activities with actor information
    ///
    /// Optionally filter metrics by date range using startDate and endDate parameters.
    /// </remarks>
    /// <param name="startDate">Optional start date for filtering metrics (filters by created date)</param>
    /// <param name="endDate">Optional end date for filtering metrics</param>
    /// <param name="ct">Cancellation token</param>
    /// <response code="200">Dashboard metrics retrieved successfully</response>
    /// <response code="401">Unauthorized - valid authentication required</response>
    /// <response code="400">Bad request - invalid date range</response>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardMetrics), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboardMetrics(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        // Validate date range if both are provided
        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            return BadRequest(new { error = "Start date cannot be after end date" });
        }

        var orgId = GetOrganizationId();

        _logger.LogInformation(
            "Retrieving dashboard metrics for organization {OrgId} with date range {StartDate} to {EndDate}",
            orgId, startDate, endDate);

        var metrics = await _dashboardService.GetDashboardMetricsAsync(orgId, startDate, endDate, ct);

        return Ok(new ApiResponse<DashboardMetrics>(true, metrics));
    }

    // ==========================================================================
    // GAP-RPT-001: Notice Analytics Endpoints
    // ==========================================================================

    /// <summary>
    /// Get summary analytics for notices.
    /// </summary>
    /// <param name="startDate">Start date (defaults to 30 days ago)</param>
    /// <param name="endDate">End date (defaults to today)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Notice analytics summary</returns>
    [HttpGet("notices/summary")]
    [ProducesResponseType(typeof(NoticeSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNoticeSummary(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!_orgService.HasPermission("reports.view"))
                return Forbid();

            var orgId = GetOrganizationId();
            var range = GetDateRange(startDate, endDate);

            var summary = await _noticeAnalyticsService.GetSummaryAsync(orgId, range, ct);

            return Ok(new NoticeSummaryResponse(summary, range));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get notice trend data over time.
    /// </summary>
    /// <param name="metric">Metric to track: count, closed, new, demand, high_priority</param>
    /// <param name="interval">Grouping interval: daily, weekly, monthly</param>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Time series data points</returns>
    [HttpGet("notices/trends")]
    [ProducesResponseType(typeof(NoticeTrendResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNoticeTrends(
        [FromQuery] string metric = "count",
        [FromQuery] string interval = "daily",
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!_orgService.HasPermission("reports.view"))
                return Forbid();

            var orgId = GetOrganizationId();
            var range = GetDateRange(startDate, endDate);

            // Validate interval
            var validIntervals = new[] { "daily", "weekly", "monthly" };
            if (!validIntervals.Contains(interval.ToLowerInvariant()))
                return BadRequest(new { error = $"Invalid interval. Valid options: {string.Join(", ", validIntervals)}" });

            var dataPoints = await _noticeAnalyticsService.GetTrendAsync(orgId, metric, range, interval, ct);

            return Ok(new NoticeTrendResponse(metric, interval, dataPoints, range));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get notice breakdown by category.
    /// </summary>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Category breakdown with counts and percentages</returns>
    [HttpGet("notices/categories")]
    [ProducesResponseType(typeof(CategoryBreakdownResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCategoryBreakdown(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!_orgService.HasPermission("reports.view"))
                return Forbid();

            var orgId = GetOrganizationId();
            var range = GetDateRange(startDate, endDate);

            var categories = await _noticeAnalyticsService.GetCategoryBreakdownAsync(orgId, range, ct);
            var totalNotices = categories.Sum(c => c.Count);

            return Ok(new CategoryBreakdownResponse(categories, totalNotices, range));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get resolution metrics including SLA compliance.
    /// </summary>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Resolution metrics</returns>
    [HttpGet("notices/resolution")]
    [ProducesResponseType(typeof(ResolutionMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetResolutionMetrics(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!_orgService.HasPermission("reports.view"))
                return Forbid();

            var orgId = GetOrganizationId();
            var range = GetDateRange(startDate, endDate);

            var metrics = await _noticeAnalyticsService.GetResolutionMetricsAsync(orgId, range, ct);

            return Ok(new ResolutionMetricsResponse(metrics, range));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // GAP-RPT-002: User Performance Endpoints
    // ==========================================================================

    /// <summary>
    /// Get performance metrics for a specific user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>User performance summary</returns>
    [HttpGet("users/{userId:guid}/performance")]
    [ProducesResponseType(typeof(UserPerformanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUserPerformance(
        Guid userId,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!_orgService.HasPermission("reports.view"))
                return Forbid();

            var orgId = GetOrganizationId();
            var range = GetDateRange(startDate, endDate);

            // Users can view their own performance, admins can view anyone's
            var currentUserId = GetUserId();
            if (userId != currentUserId && !_orgService.IsAdmin)
                return Forbid();

            var performance = await _userPerformanceService.GetUserPerformanceAsync(userId, orgId, range, ct);

            return Ok(new UserPerformanceResponse(performance, range));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get my performance metrics.
    /// </summary>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Current user's performance summary</returns>
    [HttpGet("users/me/performance")]
    [ProducesResponseType(typeof(UserPerformanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMyPerformance(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            var orgId = GetOrganizationId();
            var userId = GetUserId();
            var range = GetDateRange(startDate, endDate);

            var performance = await _userPerformanceService.GetUserPerformanceAsync(userId, orgId, range, ct);

            return Ok(new UserPerformanceResponse(performance, range));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get performance metrics for all team members.
    /// </summary>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Team performance summary</returns>
    [HttpGet("users/team")]
    [ProducesResponseType(typeof(TeamPerformanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTeamPerformance(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            // Only admins and managers can view team performance
            if (!_orgService.HasPermission("reports.view") || !_orgService.IsAdmin)
                return Forbid();

            var orgId = GetOrganizationId();
            var range = GetDateRange(startDate, endDate);

            var performances = await _userPerformanceService.GetTeamPerformanceAsync(orgId, range, ct);
            var avgSla = performances.Count > 0
                ? performances.Average(p => p.SlaAdherencePercent)
                : 0;

            return Ok(new TeamPerformanceResponse(
                Members: performances,
                TotalMembers: performances.Count,
                TeamAverageSla: Math.Round(avgSla, 2),
                Period: range
            ));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get user performance leaderboard.
    /// </summary>
    /// <param name="metric">Metric to rank by: tasks_completed, notices_handled, notices_closed, sla_adherence, response_time, workflow_transitions, comments</param>
    /// <param name="startDate">Start date</param>
    /// <param name="endDate">End date</param>
    /// <param name="limit">Maximum number of entries (default 10, max 50)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Leaderboard rankings</returns>
    [HttpGet("users/leaderboard")]
    [ProducesResponseType(typeof(LeaderboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] string metric = "tasks_completed",
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        try
        {
            if (!_orgService.HasPermission("reports.view"))
                return Forbid();

            var orgId = GetOrganizationId();
            var range = GetDateRange(startDate, endDate);

            // Validate limit
            limit = Math.Clamp(limit, 1, 50);

            var rankings = await _userPerformanceService.GetLeaderboardAsync(orgId, metric, range, limit, ct);
            var totalParticipants = await GetTotalParticipantsAsync(orgId, range, ct);

            return Ok(new LeaderboardResponse(
                Metric: metric,
                Rankings: rankings,
                TotalParticipants: totalParticipants,
                Period: range
            ));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private async Task<int> GetTotalParticipantsAsync(Guid orgId, DateRange range, CancellationToken ct)
    {
        var performances = await _userPerformanceService.GetTeamPerformanceAsync(orgId, range, ct);
        return performances.Count;
    }
}
