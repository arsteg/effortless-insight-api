using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Ca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers.Ca;

/// <summary>
/// CA notice access endpoints.
/// Requires CA client context to be set.
/// </summary>
[ApiController]
[Route("api/v1/ca/notices")]
[Authorize]
public class CaNoticesController : ControllerBase
{
    private readonly ICaNoticeService _noticeService;
    private readonly ICaAuthorizationService _authService;
    private readonly ILogger<CaNoticesController> _logger;

    public CaNoticesController(
        ICaNoticeService noticeService,
        ICaAuthorizationService authService,
        ILogger<CaNoticesController> logger)
    {
        _noticeService = noticeService;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Get notices for the selected client.
    /// Requires CA client context to be set.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CaNoticeListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNotices(
        [FromQuery] CaNoticeFilterDto filter,
        CancellationToken ct)
    {
        var context = GetCaContext();
        if (context == null)
        {
            return BadRequest(new ApiErrorResponse(false, "CLIENT_NOT_SELECTED", "Please select a client before viewing notices"));
        }

        var result = await _noticeService.GetNoticesAsync(
            context.UserId,
            context.OrganizationId,
            context.AuthorizedGstins,
            filter,
            ct);

        return Ok(new ApiResponse<CaNoticeListResponse>(true, result));
    }

    /// <summary>
    /// Get a specific notice.
    /// Requires CA client context to be set.
    /// </summary>
    [HttpGet("{noticeId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CaNoticeDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotice(
        Guid noticeId,
        CancellationToken ct)
    {
        var context = GetCaContext();
        if (context == null)
        {
            return BadRequest(new ApiErrorResponse(false, "CLIENT_NOT_SELECTED", "Please select a client before viewing notices"));
        }

        var notice = await _noticeService.GetNoticeAsync(
            context.UserId,
            noticeId,
            context.OrganizationId,
            context.AuthorizedGstins,
            ct);

        if (notice == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found or not authorized"));
        }

        return Ok(new ApiResponse<CaNoticeDetailDto>(true, notice));
    }

    /// <summary>
    /// Get notice counts for the selected client.
    /// </summary>
    [HttpGet("counts")]
    [ProducesResponseType(typeof(ApiResponse<NoticeCountsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNoticeCounts(CancellationToken ct)
    {
        var context = GetCaContext();
        if (context == null)
        {
            return BadRequest(new ApiErrorResponse(false, "CLIENT_NOT_SELECTED", "Please select a client before viewing notices"));
        }

        var (total, pending, overdue) = await _noticeService.GetNoticeCountsAsync(
            context.OrganizationId,
            context.AuthorizedGstins,
            ct);

        return Ok(new ApiResponse<NoticeCountsDto>(true, new NoticeCountsDto(total, pending, overdue)));
    }

    private CaClientContext? GetCaContext()
    {
        var userId = GetCurrentUserId();
        var orgIdClaim = User.FindFirst("ca_client_org_id")?.Value;
        var gstinsClaim = User.FindFirst("ca_authorized_gstins")?.Value;

        if (string.IsNullOrEmpty(orgIdClaim))
        {
            return null;
        }

        var authorizedGstins = !string.IsNullOrEmpty(gstinsClaim)
            ? gstinsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];

        return new CaClientContext(
            UserId: userId,
            OrganizationId: Guid.Parse(orgIdClaim),
            AuthorizedGstins: authorizedGstins
        );
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim!);
    }

    private record CaClientContext(
        Guid UserId,
        Guid OrganizationId,
        List<string> AuthorizedGstins
    );
}

public record NoticeCountsDto(int Total, int Pending, int Overdue);
