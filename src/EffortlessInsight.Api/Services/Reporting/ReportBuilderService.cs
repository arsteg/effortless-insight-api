using System.Diagnostics;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Implementation of the Custom Report Builder (GAP-RPT-003)
/// </summary>
public class ReportBuilderService : IReportBuilderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IReportQueryBuilder _queryBuilder;
    private readonly IReportExportService _exportService;
    private readonly ILogger<ReportBuilderService> _logger;

    public ReportBuilderService(
        ApplicationDbContext dbContext,
        IReportQueryBuilder queryBuilder,
        IReportExportService exportService,
        ILogger<ReportBuilderService> logger)
    {
        _dbContext = dbContext;
        _queryBuilder = queryBuilder;
        _exportService = exportService;
        _logger = logger;
    }

    // ==========================================================================
    // Saved Report CRUD
    // ==========================================================================

    public async Task<SavedReportDto> SaveReportAsync(
        Guid organizationId,
        CreateSavedReportRequest request,
        Guid userId)
    {
        // Validate report type
        if (!ReportTypes.IsValid(request.ReportType))
        {
            throw new InvalidOperationException($"INVALID_REPORT_TYPE: {request.ReportType}");
        }

        // Validate configuration
        var (isValid, errors) = await ValidateConfigurationAsync(request.ReportType, request.Configuration);
        if (!isValid)
        {
            throw new InvalidOperationException($"INVALID_CONFIGURATION: {string.Join(", ", errors)}");
        }

        var report = new SavedReport
        {
            OrganizationId = organizationId,
            CreatedById = userId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ReportType = request.ReportType.ToLowerInvariant(),
            Configuration = MapToConfiguration(request.Configuration),
            IsPublic = request.IsPublic
        };

        _dbContext.Set<SavedReport>().Add(report);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Created saved report {ReportId} '{ReportName}' for organization {OrganizationId}",
            report.Id, report.Name, organizationId);

        return await GetReportDtoAsync(report);
    }

    public async Task<SavedReportDto> GetReportAsync(Guid reportId, Guid userId)
    {
        var report = await GetReportEntityAsync(reportId, userId);
        return await GetReportDtoAsync(report);
    }

    public async Task<SavedReportListResponse> ListReportsAsync(
        Guid organizationId,
        Guid userId,
        string? reportType = null,
        bool? isPublic = null,
        string? searchTerm = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _dbContext.Set<SavedReport>()
            .Include(r => r.CreatedBy)
            .Include(r => r.Schedules)
            .Where(r => r.OrganizationId == organizationId && r.DeletedAt == null)
            .Where(r => r.IsPublic || r.CreatedById == userId); // User can see their own or public reports

        if (!string.IsNullOrEmpty(reportType))
        {
            query = query.Where(r => r.ReportType == reportType.ToLowerInvariant());
        }

        if (isPublic.HasValue)
        {
            query = query.Where(r => r.IsPublic == isPublic.Value);
        }

        if (!string.IsNullOrEmpty(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            query = query.Where(r =>
                r.Name.ToLower().Contains(term) ||
                (r.Description != null && r.Description.ToLower().Contains(term)));
        }

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var reports = await query
            .OrderByDescending(r => r.LastRunAt ?? r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new SavedReportListResponse(
            Reports: reports.Select(r => new SavedReportSummaryDto(
                Id: r.Id,
                Name: r.Name,
                Description: r.Description,
                ReportType: r.ReportType,
                IsPublic: r.IsPublic,
                LastRunAt: r.LastRunAt,
                RunCount: r.RunCount,
                HasSchedule: r.Schedules.Any(s => s.IsActive && s.DeletedAt == null),
                CreatedBy: new ReportUserDto(r.CreatedBy.Id, r.CreatedBy.Name, r.CreatedBy.Email),
                CreatedAt: r.CreatedAt
            )).ToList(),
            Pagination: new ReportPaginationDto(page, pageSize, totalItems, totalPages)
        );
    }

    public async Task<SavedReportDto> UpdateReportAsync(
        Guid reportId,
        UpdateSavedReportRequest request,
        Guid userId)
    {
        var report = await GetReportEntityAsync(reportId, userId, requireOwnership: true);

        if (!string.IsNullOrEmpty(request.Name))
        {
            report.Name = request.Name.Trim();
        }

        if (request.Description != null)
        {
            report.Description = request.Description.Trim();
        }

        if (request.Configuration != null)
        {
            var (isValid, errors) = await ValidateConfigurationAsync(report.ReportType, request.Configuration);
            if (!isValid)
            {
                throw new InvalidOperationException($"INVALID_CONFIGURATION: {string.Join(", ", errors)}");
            }
            report.Configuration = MapToConfiguration(request.Configuration);
        }

        if (request.IsPublic.HasValue)
        {
            report.IsPublic = request.IsPublic.Value;
        }

        report.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Updated saved report {ReportId}", reportId);

        return await GetReportDtoAsync(report);
    }

    public async Task DeleteReportAsync(Guid reportId, Guid userId)
    {
        var report = await GetReportEntityAsync(reportId, userId, requireOwnership: true);

        report.DeletedAt = DateTime.UtcNow;

        // Also deactivate any schedules
        var schedules = await _dbContext.Set<ReportSchedule>()
            .Where(s => s.SavedReportId == reportId && s.DeletedAt == null)
            .ToListAsync();

        foreach (var schedule in schedules)
        {
            schedule.IsActive = false;
            schedule.DeletedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted saved report {ReportId}", reportId);
    }

    // ==========================================================================
    // Report Execution
    // ==========================================================================

    public async Task<ReportExecutionResponse> ExecuteReportAsync(
        Guid reportId,
        Guid userId,
        ExecuteReportRequest? request = null)
    {
        var report = await GetReportEntityAsync(reportId, userId);
        var stopwatch = Stopwatch.StartNew();

        // Use override configuration if provided, otherwise use saved configuration
        var configuration = request?.OverrideConfiguration != null
            ? MapToConfiguration(request.OverrideConfiguration)
            : report.Configuration;

        var page = request?.Page ?? 1;
        var pageSize = request?.PageSize ?? 50;

        // Build and execute the query
        var results = await _queryBuilder.BuildAndExecuteAsync(
            report.OrganizationId,
            report.ReportType,
            configuration,
            page,
            pageSize);

        stopwatch.Stop();

        // Update last run timestamp
        report.LastRunAt = DateTime.UtcNow;
        report.RunCount++;
        await _dbContext.SaveChangesAsync();

        return new ReportExecutionResponse(
            ReportId: report.Id,
            ReportName: report.Name,
            ReportType: report.ReportType,
            ExecutedAt: DateTime.UtcNow,
            Results: results,
            Metadata: new ReportMetadataDto(
                DateRange: MapToDateRangeDto(configuration.DateRange),
                AppliedFilters: configuration.Filters.Select(f => new ReportFilterDto(f.Field, f.Operator, f.Value)).ToList(),
                ExecutionTimeMs: (int)stopwatch.ElapsedMilliseconds
            )
        );
    }

    public async Task<ReportExecutionResponse> PreviewReportAsync(
        Guid organizationId,
        string reportType,
        ReportConfigurationDto configurationDto,
        Guid userId,
        int page = 1,
        int pageSize = 50)
    {
        if (!ReportTypes.IsValid(reportType))
        {
            throw new InvalidOperationException($"INVALID_REPORT_TYPE: {reportType}");
        }

        var (isValid, errors) = await ValidateConfigurationAsync(reportType, configurationDto);
        if (!isValid)
        {
            throw new InvalidOperationException($"INVALID_CONFIGURATION: {string.Join(", ", errors)}");
        }

        var stopwatch = Stopwatch.StartNew();
        var configuration = MapToConfiguration(configurationDto);

        var results = await _queryBuilder.BuildAndExecuteAsync(
            organizationId,
            reportType.ToLowerInvariant(),
            configuration,
            page,
            pageSize);

        stopwatch.Stop();

        return new ReportExecutionResponse(
            ReportId: Guid.Empty,
            ReportName: "Preview",
            ReportType: reportType,
            ExecutedAt: DateTime.UtcNow,
            Results: results,
            Metadata: new ReportMetadataDto(
                DateRange: MapToDateRangeDto(configuration.DateRange),
                AppliedFilters: configuration.Filters.Select(f => new ReportFilterDto(f.Field, f.Operator, f.Value)).ToList(),
                ExecutionTimeMs: (int)stopwatch.ElapsedMilliseconds
            )
        );
    }

    public async Task<ReportExportResponse> ExportReportAsync(
        Guid reportId,
        Guid userId,
        string exportFormat)
    {
        if (!ExportFormats.IsValid(exportFormat))
        {
            throw new InvalidOperationException($"INVALID_EXPORT_FORMAT: {exportFormat}");
        }

        var report = await GetReportEntityAsync(reportId, userId);

        // Execute the report to get all data (no pagination for export)
        var results = await _queryBuilder.BuildAndExecuteAsync(
            report.OrganizationId,
            report.ReportType,
            report.Configuration,
            page: 1,
            pageSize: 10000); // Max rows for export

        // Export to the requested format
        var exportResult = await _exportService.ExportAsync(
            report.Name,
            report.ReportType,
            results,
            exportFormat);

        // Update last run timestamp
        report.LastRunAt = DateTime.UtcNow;
        report.RunCount++;
        await _dbContext.SaveChangesAsync();

        return exportResult;
    }

    // ==========================================================================
    // Report Schema
    // ==========================================================================

    public Task<ReportSchemaResponse> GetReportSchemaAsync(string reportType)
    {
        if (!ReportTypes.IsValid(reportType))
        {
            throw new InvalidOperationException($"INVALID_REPORT_TYPE: {reportType}");
        }

        var schema = reportType.ToLowerInvariant() switch
        {
            ReportTypes.Notices => GetNoticesSchema(),
            ReportTypes.Tasks => GetTasksSchema(),
            ReportTypes.Users => GetUsersSchema(),
            ReportTypes.Compliance => GetComplianceSchema(),
            _ => throw new InvalidOperationException($"UNSUPPORTED_REPORT_TYPE: {reportType}")
        };

        return Task.FromResult(schema);
    }

    public Task<(bool IsValid, List<string> Errors)> ValidateConfigurationAsync(
        string reportType,
        ReportConfigurationDto configuration)
    {
        var errors = new List<string>();

        // Validate columns
        if (configuration.Columns == null || !configuration.Columns.Any())
        {
            errors.Add("At least one column must be selected");
        }

        // Validate filters
        if (configuration.Filters != null)
        {
            foreach (var filter in configuration.Filters)
            {
                if (string.IsNullOrEmpty(filter.Field))
                {
                    errors.Add("Filter field is required");
                }

                if (!FilterOperators.IsValid(filter.Operator))
                {
                    errors.Add($"Invalid filter operator: {filter.Operator}");
                }
            }
        }

        // Validate date range
        if (configuration.DateRange != null)
        {
            if (configuration.DateRange.Preset == DateRangePresets.Custom)
            {
                if (!configuration.DateRange.StartDate.HasValue || !configuration.DateRange.EndDate.HasValue)
                {
                    errors.Add("Custom date range requires both start and end dates");
                }
                else if (configuration.DateRange.StartDate > configuration.DateRange.EndDate)
                {
                    errors.Add("Start date must be before end date");
                }
            }
            else if (!string.IsNullOrEmpty(configuration.DateRange.Preset) &&
                     !DateRangePresets.IsValid(configuration.DateRange.Preset))
            {
                errors.Add($"Invalid date range preset: {configuration.DateRange.Preset}");
            }
        }

        // Validate limit
        if (configuration.Limit.HasValue && configuration.Limit.Value < 1)
        {
            errors.Add("Limit must be at least 1");
        }

        return Task.FromResult((errors.Count == 0, errors));
    }

    // ==========================================================================
    // Private Methods
    // ==========================================================================

    private async Task<SavedReport> GetReportEntityAsync(
        Guid reportId,
        Guid userId,
        bool requireOwnership = false)
    {
        var report = await _dbContext.Set<SavedReport>()
            .Include(r => r.CreatedBy)
            .FirstOrDefaultAsync(r => r.Id == reportId && r.DeletedAt == null);

        if (report == null)
        {
            throw new KeyNotFoundException("REPORT_NOT_FOUND");
        }

        // Check access - user can access if they created it or it's public and in their org
        var isMember = await _dbContext.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == report.OrganizationId &&
                          m.UserId == userId &&
                          m.DeletedAt == null);

        if (!isMember)
        {
            throw new UnauthorizedAccessException("NOT_A_MEMBER");
        }

        var canAccess = report.CreatedById == userId || report.IsPublic;
        if (!canAccess)
        {
            throw new UnauthorizedAccessException("ACCESS_DENIED");
        }

        if (requireOwnership && report.CreatedById != userId)
        {
            throw new UnauthorizedAccessException("OWNER_ONLY");
        }

        return report;
    }

    private async Task<SavedReportDto> GetReportDtoAsync(SavedReport report)
    {
        if (report.CreatedBy == null)
        {
            await _dbContext.Entry(report).Reference(r => r.CreatedBy).LoadAsync();
        }

        return new SavedReportDto(
            Id: report.Id,
            Name: report.Name,
            Description: report.Description,
            ReportType: report.ReportType,
            Configuration: MapToConfigurationDto(report.Configuration),
            IsPublic: report.IsPublic,
            LastRunAt: report.LastRunAt,
            RunCount: report.RunCount,
            CreatedBy: new ReportUserDto(report.CreatedBy.Id, report.CreatedBy.Name, report.CreatedBy.Email),
            CreatedAt: report.CreatedAt,
            UpdatedAt: report.UpdatedAt
        );
    }

    private static ReportConfiguration MapToConfiguration(ReportConfigurationDto dto)
    {
        return new ReportConfiguration
        {
            Columns = dto.Columns,
            Filters = dto.Filters?.Select(f => new ReportFilter
            {
                Field = f.Field,
                Operator = f.Operator,
                Value = f.Value
            }).ToList() ?? [],
            GroupBy = dto.GroupBy,
            SortBy = dto.SortBy,
            SortDescending = dto.SortDescending,
            DateRange = dto.DateRange != null ? new DateRangeConfig
            {
                Preset = dto.DateRange.Preset,
                StartDate = dto.DateRange.StartDate,
                EndDate = dto.DateRange.EndDate
            } : null,
            Limit = dto.Limit,
            IncludeSummary = dto.IncludeSummary
        };
    }

    private static ReportConfigurationDto MapToConfigurationDto(ReportConfiguration config)
    {
        return new ReportConfigurationDto(
            Columns: config.Columns,
            Filters: config.Filters.Select(f => new ReportFilterDto(f.Field, f.Operator, f.Value)).ToList(),
            GroupBy: config.GroupBy,
            SortBy: config.SortBy,
            SortDescending: config.SortDescending,
            DateRange: config.DateRange != null ? new DateRangeConfigDto(
                config.DateRange.Preset,
                config.DateRange.StartDate,
                config.DateRange.EndDate
            ) : null,
            Limit: config.Limit,
            IncludeSummary: config.IncludeSummary
        );
    }

    private static DateRangeConfigDto? MapToDateRangeDto(DateRangeConfig? config)
    {
        return config != null
            ? new DateRangeConfigDto(config.Preset, config.StartDate, config.EndDate)
            : null;
    }

    // ==========================================================================
    // Schema Definitions
    // ==========================================================================

    private static ReportSchemaResponse GetNoticesSchema()
    {
        var columns = new List<ReportColumnDefinition>
        {
            new("noticeNumber", "Notice Number", "string", true, false, true, null, null),
            new("noticeType", "Notice Type", "enum", true, true, true, ["DRC-01", "ASMT-10", "REG-17", "SCN-01", "MOV-01"], null),
            new("noticeCategory", "Category", "enum", true, true, true, ["assessment", "demand", "registration", "refund", "audit"], null),
            new("gstin", "GSTIN", "string", true, true, true, null, null),
            new("status", "Status", "enum", true, true, true, NoticeStatus.All.ToList(), null),
            new("priority", "Priority", "enum", true, true, true, NoticePriority.All.ToList(), null),
            new("issueDate", "Issue Date", "date", true, true, true, null, null),
            new("responseDeadline", "Response Deadline", "date", true, true, true, null, null),
            new("taxAmount", "Tax Amount", "number", true, false, true, null, null),
            new("penaltyAmount", "Penalty Amount", "number", true, false, true, null, null),
            new("interestAmount", "Interest Amount", "number", true, false, true, null, null),
            new("totalDemand", "Total Demand", "number", true, false, true, null, null),
            new("assignedTo", "Assigned To", "string", true, true, true, null, null),
            new("issuingAuthority", "Issuing Authority", "string", true, true, true, null, null),
            new("financialYear", "Financial Year", "string", true, true, true, null, null),
            new("createdAt", "Created At", "date", true, false, true, null, null)
        };

        return new ReportSchemaResponse(
            ReportType: ReportTypes.Notices,
            AvailableColumns: columns,
            GroupableFields: columns.Where(c => c.IsGroupable).Select(c => c.Field).ToList(),
            SortableFields: columns.Where(c => c.IsSortable).Select(c => c.Field).ToList()
        );
    }

    private static ReportSchemaResponse GetTasksSchema()
    {
        var columns = new List<ReportColumnDefinition>
        {
            new("title", "Title", "string", true, false, true, null, null),
            new("status", "Status", "enum", true, true, true, ["todo", "in_progress", "done", "blocked", "on_hold"], null),
            new("priority", "Priority", "enum", true, true, true, ["low", "medium", "high", "critical"], null),
            new("dueDate", "Due Date", "date", true, true, true, null, null),
            new("assignee", "Assignee", "string", true, true, true, null, null),
            new("noticeNumber", "Notice Number", "string", true, true, true, null, null),
            new("noticeType", "Notice Type", "string", true, true, true, null, null),
            new("estimatedHours", "Estimated Hours", "number", true, false, true, null, null),
            new("actualHours", "Actual Hours", "number", true, false, true, null, null),
            new("isOverdue", "Is Overdue", "boolean", true, true, false, null, null),
            new("completedAt", "Completed At", "date", true, true, true, null, null),
            new("createdAt", "Created At", "date", true, false, true, null, null)
        };

        return new ReportSchemaResponse(
            ReportType: ReportTypes.Tasks,
            AvailableColumns: columns,
            GroupableFields: columns.Where(c => c.IsGroupable).Select(c => c.Field).ToList(),
            SortableFields: columns.Where(c => c.IsSortable).Select(c => c.Field).ToList()
        );
    }

    private static ReportSchemaResponse GetUsersSchema()
    {
        var columns = new List<ReportColumnDefinition>
        {
            new("name", "Name", "string", true, false, true, null, null),
            new("email", "Email", "string", true, false, true, null, null),
            new("role", "Role", "enum", true, true, true, ["owner", "admin", "member", "ca"], null),
            new("status", "Status", "enum", true, true, true, ["active", "inactive", "suspended"], null),
            new("tasksAssigned", "Tasks Assigned", "number", false, false, true, null, null),
            new("tasksCompleted", "Tasks Completed", "number", false, false, true, null, null),
            new("noticesAssigned", "Notices Assigned", "number", false, false, true, null, null),
            new("lastActiveAt", "Last Active", "date", true, true, true, null, null),
            new("joinedAt", "Joined At", "date", true, false, true, null, null)
        };

        return new ReportSchemaResponse(
            ReportType: ReportTypes.Users,
            AvailableColumns: columns,
            GroupableFields: columns.Where(c => c.IsGroupable).Select(c => c.Field).ToList(),
            SortableFields: columns.Where(c => c.IsSortable).Select(c => c.Field).ToList()
        );
    }

    private static ReportSchemaResponse GetComplianceSchema()
    {
        var columns = new List<ReportColumnDefinition>
        {
            new("gstin", "GSTIN", "string", true, true, true, null, null),
            new("totalNotices", "Total Notices", "number", false, false, true, null, null),
            new("openNotices", "Open Notices", "number", false, false, true, null, null),
            new("closedNotices", "Closed Notices", "number", false, false, true, null, null),
            new("overdueNotices", "Overdue Notices", "number", false, false, true, null, null),
            new("totalDemand", "Total Demand", "number", false, false, true, null, null),
            new("avgResponseTime", "Avg Response Time (days)", "number", false, false, true, null, null),
            new("complianceScore", "Compliance Score", "number", false, false, true, null, null),
            new("lastNoticeDate", "Last Notice Date", "date", true, false, true, null, null)
        };

        return new ReportSchemaResponse(
            ReportType: ReportTypes.Compliance,
            AvailableColumns: columns,
            GroupableFields: columns.Where(c => c.IsGroupable).Select(c => c.Field).ToList(),
            SortableFields: columns.Where(c => c.IsSortable).Select(c => c.Field).ToList()
        );
    }
}
