using System.Security.Claims;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Public activity ingestion. Anonymous visitors and authenticated users
/// post batched events here; when a valid bearer token is present the
/// events (and the visitor profile) are linked to the account. Always
/// returns 202 — tracking must never surface errors to the client.
/// </summary>
[ApiController]
[Route("api/v1/activity")]
public class ActivityController : ControllerBase
{
    private readonly IActivityTrackingService _activityService;
    private readonly IConfiguration _configuration;

    public ActivityController(
        IActivityTrackingService activityService,
        IConfiguration configuration)
    {
        _activityService = activityService;
        _configuration = configuration;
    }

    [HttpPost("track")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Track([FromBody] ActivityTrackRequest request)
    {
        if (!_configuration.GetValue("Analytics:Enabled", true))
        {
            return Accepted();
        }

        Guid? userId = null;
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var parsed))
        {
            userId = parsed;
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        await _activityService.TrackBatchAsync(request, userId, userAgent);

        return Accepted();
    }
}
