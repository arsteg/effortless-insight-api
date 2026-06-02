using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class NoticesController : ControllerBase
{
    private readonly INoticeService _noticeService;
    private readonly ILogger<NoticesController> _logger;

    public NoticesController(INoticeService noticeService, ILogger<NoticesController> logger)
    {
        _noticeService = noticeService;
        _logger = logger;
    }

    /// <summary>
    /// Upload a new notice for AI processing
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB limit
    public async Task<ActionResult<NoticeDto>> Upload([FromForm] CreateNoticeDto dto)
    {
        var userId = GetUserId();
        var notice = await _noticeService.CreateAsync(dto, userId);

        _logger.LogInformation("Notice {NoticeId} uploaded by user {UserId}", notice.Id, userId);

        // Trigger AI processing in background
        await _noticeService.TriggerAiProcessingAsync(notice.Id);

        return CreatedAtAction(nameof(GetById), new { id = notice.Id }, MapToDto(notice));
    }

    /// <summary>
    /// Get all notices for the current organization
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<NoticeDto>>> GetAll([FromQuery] NoticeFilterDto filter)
    {
        var organizationId = GetOrganizationId();
        var result = await _noticeService.GetByOrganizationAsync(organizationId, filter);

        return Ok(new PagedResult<NoticeDto>(
            result.Items.Select(MapToDto).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize,
            result.TotalPages
        ));
    }

    /// <summary>
    /// Get a specific notice by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NoticeDetailDto>> GetById(Guid id)
    {
        var notice = await _noticeService.GetByIdAsync(id);
        if (notice == null)
            return NotFound();

        return Ok(MapToDetailDto(notice));
    }

    /// <summary>
    /// Update a notice
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NoticeDto>> Update(Guid id, [FromBody] UpdateNoticeDto dto)
    {
        var notice = await _noticeService.UpdateAsync(id, dto);
        return Ok(MapToDto(notice));
    }

    /// <summary>
    /// Delete a notice (soft delete)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _noticeService.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Get AI analysis report for a notice
    /// </summary>
    [HttpGet("{id:guid}/report")]
    public async Task<ActionResult<NoticeAiReportDto>> GetReport(Guid id)
    {
        var report = await _noticeService.GetReportAsync(id);
        if (report == null)
            return NotFound();

        return Ok(MapToReportDto(report));
    }

    /// <summary>
    /// Regenerate AI analysis for a notice
    /// </summary>
    [HttpPost("{id:guid}/regenerate-report")]
    public async Task<IActionResult> RegenerateReport(Guid id)
    {
        await _noticeService.TriggerAiProcessingAsync(id);
        return Accepted();
    }

    // Helper methods
    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");
        return Guid.Parse(userIdClaim);
    }

    private Guid GetOrganizationId()
    {
        var orgIdClaim = User.FindFirst("org_id")?.Value
            ?? throw new UnauthorizedAccessException("Organization ID not found in token");
        return Guid.Parse(orgIdClaim);
    }

    private static NoticeDto MapToDto(Data.Entities.Notice notice)
    {
        var daysRemaining = notice.ResponseDeadline.HasValue
            ? (int?)(notice.ResponseDeadline.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days
            : null;

        return new NoticeDto(
            notice.Id,
            notice.NoticeType,
            notice.NoticeCategory,
            notice.NoticeNumber,
            notice.Gstin,
            notice.IssueDate,
            notice.ResponseDeadline,
            daysRemaining,
            notice.TaxAmount,
            notice.PenaltyAmount,
            notice.Status,
            notice.Priority,
            notice.AiReport?.RiskScore,
            notice.AiReport?.RiskLevel,
            notice.AiReport?.SummaryEn,
            notice.AssignedToId,
            notice.AssignedTo?.Name,
            notice.CreatedAt
        );
    }

    private static NoticeDetailDto MapToDetailDto(Data.Entities.Notice notice)
    {
        var daysRemaining = notice.ResponseDeadline.HasValue
            ? (int?)(notice.ResponseDeadline.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days
            : null;

        return new NoticeDetailDto(
            notice.Id,
            notice.NoticeType,
            notice.NoticeCategory,
            notice.NoticeNumber,
            notice.Gstin,
            notice.IssueDate,
            notice.ResponseDeadline,
            notice.ExtendedDeadline,
            daysRemaining,
            notice.TaxAmount,
            notice.PenaltyAmount,
            notice.InterestAmount,
            notice.PeriodFrom,
            notice.PeriodTo,
            notice.IssuingAuthority,
            notice.Status,
            notice.Priority,
            notice.FileUrl,
            notice.ProcessingStatus,
            notice.Tags,
            notice.AiReport != null ? MapToReportDto(notice.AiReport) : null,
            notice.AssignedToId,
            notice.AssignedTo?.Name,
            notice.CreatedAt,
            notice.UpdatedAt
        );
    }

    private static NoticeAiReportDto MapToReportDto(Data.Entities.NoticeAiReport report)
    {
        return new NoticeAiReportDto(
            report.Id,
            report.RiskScore,
            report.RiskLevel,
            report.SummaryEn,
            report.SummaryHi,
            report.PlainEnglish,
            null, // ActionItems - parse from JSON
            null, // RequiredDocuments - parse from JSON
            null, // LegalReferences - parse from JSON
            null, // ConfidenceScores - parse from JSON
            report.ModelUsed,
            report.ProcessingTimeMs,
            report.CreatedAt
        );
    }
}
