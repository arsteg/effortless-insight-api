using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Collaboration;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1")]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _activityService;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<ActivityController> _logger;

    public ActivityController(
        IActivityService activityService,
        ICurrentOrganizationService orgService,
        ILogger<ActivityController> logger)
    {
        _activityService = activityService;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);

    /// <summary>
    /// Get activity feed for a notice
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/activity")]
    [ProducesResponseType(typeof(ActivityFeedResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivityFeedForNotice(
        Guid noticeId,
        [FromQuery] string? types = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] int limit = 50)
    {
        var typeList = string.IsNullOrEmpty(types)
            ? null
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var result = await _activityService.GetActivityFeedForNoticeAsync(
            noticeId, GetUserId(), typeList, since, limit);

        return Ok(result);
    }

    /// <summary>
    /// Get activity feed for the organization
    /// </summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(ActivityFeedResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivityFeedForOrganization(
        [FromQuery] string? types = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] int limit = 50)
    {
        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");

        var typeList = string.IsNullOrEmpty(types)
            ? null
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

        var result = await _activityService.GetActivityFeedForOrganizationAsync(
            orgId, GetUserId(), typeList, since, limit);

        return Ok(result);
    }
}
