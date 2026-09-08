using EffortlessInsight.Api.Services.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin activity analytics: portal visitor and user activity across the
/// whole product — overview KPIs, trends, top pages/events, filtered event
/// feed, visitor list, and per-visitor/user chronological journeys.
/// </summary>
[Route("api/v1/admin/activity")]
public class AdminActivityController : AdminControllerBase
{
    private readonly IActivityTrackingService _activityService;

    public AdminActivityController(
        IActivityTrackingService activityService,
        ILogger<AdminActivityController> logger)
        : base(logger)
    {
        _activityService = activityService;
    }

    /// <summary>Normalizes the date range: defaults to the last 30 days, caps at 366.</summary>
    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        var end = (to?.ToUniversalTime() ?? DateTime.UtcNow).AddSeconds(1);
        var start = from?.ToUniversalTime() ?? end.AddDays(-30);
        if (start >= end)
        {
            start = end.AddDays(-30);
        }
        if ((end - start).TotalDays > 366)
        {
            start = end.AddDays(-366);
        }
        return (start, end);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var (start, end) = Range(from, to);
        return Success(await _activityService.GetOverviewAsync(start, end));
    }

    [HttpGet("trends")]
    public async Task<IActionResult> Trends([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var (start, end) = Range(from, to);
        return Success(await _activityService.GetTrendsAsync(start, end));
    }

    [HttpGet("top-pages")]
    public async Task<IActionResult> TopPages(
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] int limit = 10)
    {
        var (start, end) = Range(from, to);
        return Success(await _activityService.GetTopPagesAsync(start, end, Math.Clamp(limit, 1, 50)));
    }

    [HttpGet("top-events")]
    public async Task<IActionResult> TopEvents(
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] int limit = 10)
    {
        var (start, end) = Range(from, to);
        return Success(await _activityService.GetTopEventsAsync(start, end, Math.Clamp(limit, 1, 50)));
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? visitorId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? page = null,
        [FromQuery] bool? authenticated = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var (start, end) = Range(from, to);
        var result = await _activityService.GetEventsAsync(
            start, end, visitorId, userId, eventType, page, authenticated,
            Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 200));
        return Success(result);
    }

    [HttpGet("visitors")]
    public async Task<IActionResult> Visitors(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool? authenticated = null,
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25)
    {
        var (start, end) = Range(from, to);
        var result = await _activityService.GetVisitorsAsync(
            start, end, authenticated, search,
            Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 100));
        return Success(result);
    }

    /// <summary>
    /// Chronological journey for one visitor or user, including anonymous
    /// activity from before signup when the identities were linked.
    /// </summary>
    [HttpGet("journey")]
    public async Task<IActionResult> Journey(
        [FromQuery] string? visitorId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 200)
    {
        if (string.IsNullOrWhiteSpace(visitorId) && userId == null)
        {
            return Error("visitorId or userId is required", "VALIDATION");
        }

        var journey = await _activityService.GetJourneyAsync(
            visitorId, userId, Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 500));
        if (journey == null)
        {
            return NotFoundResponse("Visitor not found");
        }

        return Success(journey);
    }
}
