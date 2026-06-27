using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Controller for custom report builder and scheduled reports.
/// Implements GAP-RPT-003 (Custom Report Builder) and GAP-RPT-006 (Scheduled Reports).
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/reports")]
public class ReportingController : ControllerBase
{
    private readonly IReportBuilderService _reportService;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ReportingController> _logger;

    public ReportingController(
        IReportBuilderService reportService,
        ICurrentOrganizationService orgService,
        ApplicationDbContext dbContext,
        ILogger<ReportingController> logger)
    {
        _reportService = reportService;
        _orgService = orgService;
        _dbContext = dbContext;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);

    private Guid GetOrganizationId() =>
        _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");

    // ==========================================================================
    // Report Types & Schema
    // ==========================================================================

    /// <summary>
    /// Get available report types.
    /// </summary>
    [HttpGet("types")]
    [ProducesResponseType(typeof(ReportTypesResponse), StatusCodes.Status200OK)]
    public IActionResult GetReportTypes()
    {
        var types = new List<ReportTypeInfo>
        {
            new(ReportTypes.Notices, "Notices Report", "Generate reports on tax notices with filtering and grouping"),
            new(ReportTypes.Tasks, "Tasks Report", "Generate reports on tasks with status and assignment tracking"),
            new(ReportTypes.Users, "Users Report", "Generate reports on team members and their activity"),
            new(ReportTypes.Compliance, "Compliance Report", "Generate compliance summary reports by GSTIN")
        };

        return Ok(new ReportTypesResponse(types));
    }

    /// <summary>
    /// Get schema for a specific report type (available columns, filters, etc.).
    /// </summary>
    [HttpGet("types/{reportType}/schema")]
    [ProducesResponseType(typeof(ReportSchemaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReportSchema(string reportType)
    {
        try
        {
            var schema = await _reportService.GetReportSchemaAsync(reportType);
            return Ok(schema);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_REPORT_TYPE"))
        {
            return BadRequest(new { error = "Invalid report type", reportType });
        }
    }

    // ==========================================================================
    // Saved Reports CRUD
    // ==========================================================================

    /// <summary>
    /// List all saved reports for the current organization.
    /// </summary>
    [HttpGet("definitions")]
    [ProducesResponseType(typeof(SavedReportListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListReports(
        [FromQuery] string? reportType = null,
        [FromQuery] bool? isPublic = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!_orgService.HasPermission("reports.view"))
            return Forbid();

        var orgId = GetOrganizationId();
        var userId = GetUserId();

        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await _reportService.ListReportsAsync(
            orgId, userId, reportType, isPublic, search, page, pageSize);

        return Ok(result);
    }

    /// <summary>
    /// Create a new saved report.
    /// </summary>
    [HttpPost("definitions")]
    [ProducesResponseType(typeof(SavedReportDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateReport([FromBody] CreateSavedReportRequest request)
    {
        if (!_orgService.HasPermission("reports.create"))
            return Forbid();

        try
        {
            var orgId = GetOrganizationId();
            var userId = GetUserId();

            var report = await _reportService.SaveReportAsync(orgId, request, userId);

            _logger.LogInformation(
                "Created report {ReportId} '{ReportName}' for organization {OrgId}",
                report.Id, report.Name, orgId);

            return CreatedAtAction(nameof(GetReport), new { id = report.Id }, report);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_"))
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific saved report by ID.
    /// </summary>
    [HttpGet("definitions/{id:guid}")]
    [ProducesResponseType(typeof(SavedReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReport(Guid id)
    {
        if (!_orgService.HasPermission("reports.view"))
            return Forbid();

        try
        {
            var userId = GetUserId();
            var report = await _reportService.GetReportAsync(id, userId);
            return Ok(report);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Report not found" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Update a saved report.
    /// </summary>
    [HttpPut("definitions/{id:guid}")]
    [ProducesResponseType(typeof(SavedReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateReport(Guid id, [FromBody] UpdateSavedReportRequest request)
    {
        if (!_orgService.HasPermission("reports.update"))
            return Forbid();

        try
        {
            var userId = GetUserId();
            var report = await _reportService.UpdateReportAsync(id, request, userId);
            return Ok(report);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Report not found" });
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "OWNER_ONLY")
        {
            return BadRequest(new { error = "Only the report owner can update this report" });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_"))
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a saved report.
    /// </summary>
    [HttpDelete("definitions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteReport(Guid id)
    {
        if (!_orgService.HasPermission("reports.delete"))
            return Forbid();

        try
        {
            var userId = GetUserId();
            await _reportService.DeleteReportAsync(id, userId);

            _logger.LogInformation("Deleted report {ReportId}", id);

            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Report not found" });
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "OWNER_ONLY")
        {
            return BadRequest(new { error = "Only the report owner can delete this report" });
        }
    }

    // ==========================================================================
    // Report Execution
    // ==========================================================================

    /// <summary>
    /// Execute a saved report and return results.
    /// </summary>
    [HttpPost("definitions/{id:guid}/execute")]
    [ProducesResponseType(typeof(ReportExecutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExecuteReport(
        Guid id,
        [FromBody] ExecuteReportRequest? request = null)
    {
        if (!_orgService.HasPermission("reports.execute"))
            return Forbid();

        try
        {
            var userId = GetUserId();
            var result = await _reportService.ExecuteReportAsync(id, userId, request);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Report not found" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Preview a report configuration without saving.
    /// </summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ReportExecutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewReport([FromBody] PreviewReportRequest request)
    {
        if (!_orgService.HasPermission("reports.view"))
            return Forbid();

        try
        {
            var orgId = GetOrganizationId();
            var userId = GetUserId();

            var result = await _reportService.PreviewReportAsync(
                orgId,
                request.ReportType,
                request.Configuration,
                userId,
                request.Page,
                request.PageSize);

            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_"))
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Export a saved report to the specified format.
    /// </summary>
    [HttpPost("definitions/{id:guid}/export")]
    [ProducesResponseType(typeof(ReportExportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportReport(Guid id, [FromBody] ExportReportFormatRequest request)
    {
        if (!_orgService.HasPermission("reports.export"))
            return Forbid();

        try
        {
            var userId = GetUserId();
            var result = await _reportService.ExportReportAsync(id, userId, request.Format);

            // Set the report ID in the response
            result = result with { ReportId = id };

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Report not found" });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INVALID_"))
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Report Schedules
    // ==========================================================================

    /// <summary>
    /// List all report schedules for the current organization.
    /// </summary>
    [HttpGet("schedules")]
    [ProducesResponseType(typeof(ReportScheduleListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSchedules(
        [FromQuery] Guid? reportId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!_orgService.HasPermission("reports.view"))
            return Forbid();

        var orgId = GetOrganizationId();
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.Set<ReportSchedule>()
            .Include(s => s.SavedReport)
            .Include(s => s.CreatedBy)
            .Where(s => s.SavedReport.OrganizationId == orgId && s.DeletedAt == null);

        if (reportId.HasValue)
        {
            query = query.Where(s => s.SavedReportId == reportId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(s => s.IsActive == isActive.Value);
        }

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var schedules = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = schedules.Select(s => new ReportScheduleDto(
            Id: s.Id,
            SavedReportId: s.SavedReportId,
            ReportName: s.SavedReport.Name,
            Frequency: s.Frequency,
            DayOfWeek: s.DayOfWeek,
            DayOfMonth: s.DayOfMonth,
            TimeOfDay: s.TimeOfDay,
            TimeZone: s.TimeZone,
            Recipients: s.Recipients,
            ExportFormat: s.ExportFormat,
            IsActive: s.IsActive,
            LastRunAt: s.LastRunAt,
            NextRunAt: s.NextRunAt,
            LastRunStatus: s.LastRunStatus,
            LastRunError: s.LastRunError,
            CreatedBy: new ReportUserDto(s.CreatedBy.Id, s.CreatedBy.Name, s.CreatedBy.Email),
            CreatedAt: s.CreatedAt
        )).ToList();

        return Ok(new ReportScheduleListResponse(
            Schedules: dtos,
            Pagination: new ReportPaginationDto(page, pageSize, totalItems, totalPages)
        ));
    }

    /// <summary>
    /// Create a new report schedule.
    /// </summary>
    [HttpPost("schedules")]
    [ProducesResponseType(typeof(ReportScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSchedule([FromBody] CreateReportScheduleRequest request)
    {
        if (!_orgService.HasPermission("reports.schedule"))
            return Forbid();

        var orgId = GetOrganizationId();
        var userId = GetUserId();

        // Validate the saved report exists and belongs to this org
        var report = await _dbContext.Set<SavedReport>()
            .FirstOrDefaultAsync(r => r.Id == request.SavedReportId &&
                                      r.OrganizationId == orgId &&
                                      r.DeletedAt == null);

        if (report == null)
        {
            return BadRequest(new { error = "Report not found or not accessible" });
        }

        // Validate frequency
        if (!ScheduleFrequency.IsValid(request.Frequency))
        {
            return BadRequest(new { error = $"Invalid frequency. Valid options: {string.Join(", ", ScheduleFrequency.All)}" });
        }

        // Validate export format
        if (!ExportFormats.IsValid(request.ExportFormat))
        {
            return BadRequest(new { error = $"Invalid export format. Valid options: {string.Join(", ", ExportFormats.All)}" });
        }

        // Validate recipients
        if (request.Recipients == null || !request.Recipients.Any())
        {
            return BadRequest(new { error = "At least one recipient email is required" });
        }

        var schedule = new ReportSchedule
        {
            SavedReportId = request.SavedReportId,
            Frequency = request.Frequency.ToLowerInvariant(),
            DayOfWeek = request.DayOfWeek,
            DayOfMonth = request.DayOfMonth,
            TimeOfDay = request.TimeOfDay,
            TimeZone = request.TimeZone ?? "Asia/Kolkata",
            Recipients = request.Recipients,
            ExportFormat = request.ExportFormat.ToLowerInvariant(),
            IsActive = request.IsActive,
            CreatedById = userId,
            NextRunAt = CalculateNextRunTime(request)
        };

        _dbContext.Set<ReportSchedule>().Add(schedule);
        await _dbContext.SaveChangesAsync();

        // Reload with navigation properties
        await _dbContext.Entry(schedule).Reference(s => s.SavedReport).LoadAsync();
        await _dbContext.Entry(schedule).Reference(s => s.CreatedBy).LoadAsync();

        _logger.LogInformation(
            "Created schedule {ScheduleId} for report {ReportId}",
            schedule.Id, request.SavedReportId);

        var dto = new ReportScheduleDto(
            Id: schedule.Id,
            SavedReportId: schedule.SavedReportId,
            ReportName: schedule.SavedReport.Name,
            Frequency: schedule.Frequency,
            DayOfWeek: schedule.DayOfWeek,
            DayOfMonth: schedule.DayOfMonth,
            TimeOfDay: schedule.TimeOfDay,
            TimeZone: schedule.TimeZone,
            Recipients: schedule.Recipients,
            ExportFormat: schedule.ExportFormat,
            IsActive: schedule.IsActive,
            LastRunAt: schedule.LastRunAt,
            NextRunAt: schedule.NextRunAt,
            LastRunStatus: schedule.LastRunStatus,
            LastRunError: schedule.LastRunError,
            CreatedBy: new ReportUserDto(schedule.CreatedBy.Id, schedule.CreatedBy.Name, schedule.CreatedBy.Email),
            CreatedAt: schedule.CreatedAt
        );

        return CreatedAtAction(nameof(GetSchedule), new { id = schedule.Id }, dto);
    }

    /// <summary>
    /// Get a specific schedule by ID.
    /// </summary>
    [HttpGet("schedules/{id:guid}")]
    [ProducesResponseType(typeof(ReportScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSchedule(Guid id)
    {
        if (!_orgService.HasPermission("reports.view"))
            return Forbid();

        var orgId = GetOrganizationId();

        var schedule = await _dbContext.Set<ReportSchedule>()
            .Include(s => s.SavedReport)
            .Include(s => s.CreatedBy)
            .FirstOrDefaultAsync(s => s.Id == id &&
                                      s.SavedReport.OrganizationId == orgId &&
                                      s.DeletedAt == null);

        if (schedule == null)
        {
            return NotFound(new { error = "Schedule not found" });
        }

        var dto = new ReportScheduleDto(
            Id: schedule.Id,
            SavedReportId: schedule.SavedReportId,
            ReportName: schedule.SavedReport.Name,
            Frequency: schedule.Frequency,
            DayOfWeek: schedule.DayOfWeek,
            DayOfMonth: schedule.DayOfMonth,
            TimeOfDay: schedule.TimeOfDay,
            TimeZone: schedule.TimeZone,
            Recipients: schedule.Recipients,
            ExportFormat: schedule.ExportFormat,
            IsActive: schedule.IsActive,
            LastRunAt: schedule.LastRunAt,
            NextRunAt: schedule.NextRunAt,
            LastRunStatus: schedule.LastRunStatus,
            LastRunError: schedule.LastRunError,
            CreatedBy: new ReportUserDto(schedule.CreatedBy.Id, schedule.CreatedBy.Name, schedule.CreatedBy.Email),
            CreatedAt: schedule.CreatedAt
        );

        return Ok(dto);
    }

    /// <summary>
    /// Update a report schedule.
    /// </summary>
    [HttpPut("schedules/{id:guid}")]
    [ProducesResponseType(typeof(ReportScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSchedule(Guid id, [FromBody] UpdateReportScheduleRequest request)
    {
        if (!_orgService.HasPermission("reports.schedule"))
            return Forbid();

        var orgId = GetOrganizationId();

        var schedule = await _dbContext.Set<ReportSchedule>()
            .Include(s => s.SavedReport)
            .Include(s => s.CreatedBy)
            .FirstOrDefaultAsync(s => s.Id == id &&
                                      s.SavedReport.OrganizationId == orgId &&
                                      s.DeletedAt == null);

        if (schedule == null)
        {
            return NotFound(new { error = "Schedule not found" });
        }

        // Apply updates
        if (!string.IsNullOrEmpty(request.Frequency))
        {
            if (!ScheduleFrequency.IsValid(request.Frequency))
            {
                return BadRequest(new { error = "Invalid frequency" });
            }
            schedule.Frequency = request.Frequency.ToLowerInvariant();
        }

        if (request.DayOfWeek.HasValue)
        {
            schedule.DayOfWeek = request.DayOfWeek;
        }

        if (request.DayOfMonth.HasValue)
        {
            schedule.DayOfMonth = request.DayOfMonth;
        }

        if (request.TimeOfDay.HasValue)
        {
            schedule.TimeOfDay = request.TimeOfDay.Value;
        }

        if (!string.IsNullOrEmpty(request.TimeZone))
        {
            schedule.TimeZone = request.TimeZone;
        }

        if (request.Recipients != null)
        {
            if (!request.Recipients.Any())
            {
                return BadRequest(new { error = "At least one recipient is required" });
            }
            schedule.Recipients = request.Recipients;
        }

        if (!string.IsNullOrEmpty(request.ExportFormat))
        {
            if (!ExportFormats.IsValid(request.ExportFormat))
            {
                return BadRequest(new { error = "Invalid export format" });
            }
            schedule.ExportFormat = request.ExportFormat.ToLowerInvariant();
        }

        if (request.IsActive.HasValue)
        {
            schedule.IsActive = request.IsActive.Value;
        }

        // Recalculate next run time
        schedule.NextRunAt = CalculateNextRunTimeFromSchedule(schedule);
        schedule.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Updated schedule {ScheduleId}", id);

        var dto = new ReportScheduleDto(
            Id: schedule.Id,
            SavedReportId: schedule.SavedReportId,
            ReportName: schedule.SavedReport.Name,
            Frequency: schedule.Frequency,
            DayOfWeek: schedule.DayOfWeek,
            DayOfMonth: schedule.DayOfMonth,
            TimeOfDay: schedule.TimeOfDay,
            TimeZone: schedule.TimeZone,
            Recipients: schedule.Recipients,
            ExportFormat: schedule.ExportFormat,
            IsActive: schedule.IsActive,
            LastRunAt: schedule.LastRunAt,
            NextRunAt: schedule.NextRunAt,
            LastRunStatus: schedule.LastRunStatus,
            LastRunError: schedule.LastRunError,
            CreatedBy: new ReportUserDto(schedule.CreatedBy.Id, schedule.CreatedBy.Name, schedule.CreatedBy.Email),
            CreatedAt: schedule.CreatedAt
        );

        return Ok(dto);
    }

    /// <summary>
    /// Delete a report schedule.
    /// </summary>
    [HttpDelete("schedules/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSchedule(Guid id)
    {
        if (!_orgService.HasPermission("reports.schedule"))
            return Forbid();

        var orgId = GetOrganizationId();

        var schedule = await _dbContext.Set<ReportSchedule>()
            .Include(s => s.SavedReport)
            .FirstOrDefaultAsync(s => s.Id == id &&
                                      s.SavedReport.OrganizationId == orgId &&
                                      s.DeletedAt == null);

        if (schedule == null)
        {
            return NotFound(new { error = "Schedule not found" });
        }

        schedule.DeletedAt = DateTime.UtcNow;
        schedule.IsActive = false;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted schedule {ScheduleId}", id);

        return NoContent();
    }

    // ==========================================================================
    // Dashboard Metrics
    // ==========================================================================

    /// <summary>
    /// Get report schedule summary metrics.
    /// </summary>
    [HttpGet("schedules/summary")]
    [ProducesResponseType(typeof(ReportScheduleSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScheduleSummary()
    {
        if (!_orgService.HasPermission("reports.view"))
            return Forbid();

        var orgId = GetOrganizationId();

        var schedules = await _dbContext.Set<ReportSchedule>()
            .Include(s => s.SavedReport)
            .Where(s => s.SavedReport.OrganizationId == orgId && s.DeletedAt == null)
            .ToListAsync();

        var summary = new ReportScheduleSummaryDto(
            TotalSchedules: schedules.Count,
            ActiveSchedules: schedules.Count(s => s.IsActive),
            FailedLastRun: schedules.Count(s => s.LastRunStatus == ScheduleRunStatus.Failed),
            NextScheduledRun: schedules
                .Where(s => s.IsActive && s.NextRunAt.HasValue)
                .OrderBy(s => s.NextRunAt)
                .Select(s => s.NextRunAt)
                .FirstOrDefault()
        );

        return Ok(summary);
    }

    // ==========================================================================
    // Private Helpers
    // ==========================================================================

    private static DateTime? CalculateNextRunTime(CreateReportScheduleRequest request)
    {
        if (!request.IsActive)
            return null;

        var now = DateTime.UtcNow;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone ?? "Asia/Kolkata");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone);

        DateTime nextRun;
        var targetTime = request.TimeOfDay;

        switch (request.Frequency.ToLowerInvariant())
        {
            case ScheduleFrequency.Daily:
                nextRun = localNow.Date.Add(targetTime.ToTimeSpan());
                if (nextRun <= localNow)
                    nextRun = nextRun.AddDays(1);
                break;

            case ScheduleFrequency.Weekly:
                var dayOfWeek = request.DayOfWeek ?? 1; // Default Monday
                var daysUntilTarget = ((dayOfWeek - (int)localNow.DayOfWeek) + 7) % 7;
                nextRun = localNow.Date.AddDays(daysUntilTarget).Add(targetTime.ToTimeSpan());
                if (nextRun <= localNow)
                    nextRun = nextRun.AddDays(7);
                break;

            case ScheduleFrequency.Monthly:
                var dayOfMonth = Math.Min(request.DayOfMonth ?? 1, DateTime.DaysInMonth(localNow.Year, localNow.Month));
                nextRun = new DateTime(localNow.Year, localNow.Month, dayOfMonth).Add(targetTime.ToTimeSpan());
                if (nextRun <= localNow)
                    nextRun = nextRun.AddMonths(1);
                break;

            default:
                return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(nextRun, timeZone);
    }

    private static DateTime? CalculateNextRunTimeFromSchedule(ReportSchedule schedule)
    {
        if (!schedule.IsActive)
            return null;

        var now = DateTime.UtcNow;
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        }
        catch
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone);

        DateTime nextRun;
        var targetTime = schedule.TimeOfDay;

        switch (schedule.Frequency)
        {
            case ScheduleFrequency.Daily:
                nextRun = localNow.Date.Add(targetTime.ToTimeSpan());
                if (nextRun <= localNow)
                    nextRun = nextRun.AddDays(1);
                break;

            case ScheduleFrequency.Weekly:
                var dayOfWeek = schedule.DayOfWeek ?? 1;
                var daysUntilTarget = ((dayOfWeek - (int)localNow.DayOfWeek) + 7) % 7;
                nextRun = localNow.Date.AddDays(daysUntilTarget).Add(targetTime.ToTimeSpan());
                if (nextRun <= localNow)
                    nextRun = nextRun.AddDays(7);
                break;

            case ScheduleFrequency.Monthly:
                var dayOfMonth = Math.Min(schedule.DayOfMonth ?? 1, DateTime.DaysInMonth(localNow.Year, localNow.Month));
                nextRun = new DateTime(localNow.Year, localNow.Month, dayOfMonth).Add(targetTime.ToTimeSpan());
                if (nextRun <= localNow)
                    nextRun = nextRun.AddMonths(1);
                break;

            default:
                return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(nextRun, timeZone);
    }
}

// ==========================================================================
// Supporting DTOs
// ==========================================================================

public record ReportTypesResponse(List<ReportTypeInfo> Types);

public record ReportTypeInfo(string Type, string Name, string Description);

public record PreviewReportRequest(
    string ReportType,
    ReportConfigurationDto Configuration,
    int Page = 1,
    int PageSize = 50
);

public record ExportReportFormatRequest(string Format);
