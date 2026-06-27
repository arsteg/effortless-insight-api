namespace EffortlessInsight.Api.DTOs;

// =============================================================================
// SAVED REPORT DTOs (GAP-RPT-003)
// =============================================================================

public record CreateSavedReportRequest(
    string Name,
    string? Description,
    string ReportType,
    ReportConfigurationDto Configuration,
    bool IsPublic
);

public record UpdateSavedReportRequest(
    string? Name,
    string? Description,
    ReportConfigurationDto? Configuration,
    bool? IsPublic
);

public record SavedReportDto(
    Guid Id,
    string Name,
    string? Description,
    string ReportType,
    ReportConfigurationDto Configuration,
    bool IsPublic,
    DateTime? LastRunAt,
    int RunCount,
    ReportUserDto CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record SavedReportListResponse(
    List<SavedReportSummaryDto> Reports,
    ReportPaginationDto Pagination
);

public record SavedReportSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string ReportType,
    bool IsPublic,
    DateTime? LastRunAt,
    int RunCount,
    bool HasSchedule,
    ReportUserDto CreatedBy,
    DateTime CreatedAt
);

public record ReportConfigurationDto(
    List<string> Columns,
    List<ReportFilterDto>? Filters,
    string? GroupBy,
    string? SortBy,
    bool SortDescending,
    DateRangeConfigDto? DateRange,
    int? Limit,
    bool IncludeSummary
);

public record ReportFilterDto(
    string Field,
    string Operator,
    object? Value
);

public record DateRangeConfigDto(
    string? Preset,
    DateOnly? StartDate,
    DateOnly? EndDate
);

public record ReportUserDto(
    Guid Id,
    string Name,
    string? Email
);

// =============================================================================
// REPORT EXECUTION DTOs
// =============================================================================

public record ExecuteReportRequest(
    ReportConfigurationDto? OverrideConfiguration,
    string? ExportFormat, // null for JSON response, or "pdf", "excel", "csv"
    int Page = 1,
    int PageSize = 50
);

public record ReportExecutionResponse(
    Guid ReportId,
    string ReportName,
    string ReportType,
    DateTime ExecutedAt,
    ReportResultDto Results,
    ReportMetadataDto Metadata
);

public record ReportResultDto(
    List<string> Columns,
    List<Dictionary<string, object?>> Rows,
    ReportSummaryDto? Summary,
    ReportPaginationDto? Pagination
);

public record ReportSummaryDto(
    int TotalCount,
    Dictionary<string, object?> Aggregations
);

public record ReportMetadataDto(
    DateRangeConfigDto? DateRange,
    List<ReportFilterDto>? AppliedFilters,
    int ExecutionTimeMs
);

public record ReportExportResponse(
    Guid ReportId,
    string FileName,
    string ContentType,
    string DownloadUrl,
    DateTime ExpiresAt
);

// =============================================================================
// REPORT SCHEDULE DTOs (GAP-RPT-006)
// =============================================================================

public record CreateReportScheduleRequest(
    Guid SavedReportId,
    string Frequency,
    int? DayOfWeek,
    int? DayOfMonth,
    TimeOnly TimeOfDay,
    string? TimeZone,
    List<string> Recipients,
    string ExportFormat,
    bool IsActive
);

public record UpdateReportScheduleRequest(
    string? Frequency,
    int? DayOfWeek,
    int? DayOfMonth,
    TimeOnly? TimeOfDay,
    string? TimeZone,
    List<string>? Recipients,
    string? ExportFormat,
    bool? IsActive
);

public record ReportScheduleDto(
    Guid Id,
    Guid SavedReportId,
    string ReportName,
    string Frequency,
    int? DayOfWeek,
    int? DayOfMonth,
    TimeOnly TimeOfDay,
    string TimeZone,
    List<string> Recipients,
    string ExportFormat,
    bool IsActive,
    DateTime? LastRunAt,
    DateTime? NextRunAt,
    string? LastRunStatus,
    string? LastRunError,
    ReportUserDto CreatedBy,
    DateTime CreatedAt
);

public record ReportScheduleListResponse(
    List<ReportScheduleDto> Schedules,
    ReportPaginationDto Pagination
);

public record ReportScheduleSummaryDto(
    int TotalSchedules,
    int ActiveSchedules,
    int FailedLastRun,
    DateTime? NextScheduledRun
);

// =============================================================================
// AVAILABLE COLUMNS/FIELDS DTOs
// =============================================================================

public record ReportSchemaResponse(
    string ReportType,
    List<ReportColumnDefinition> AvailableColumns,
    List<string> GroupableFields,
    List<string> SortableFields
);

public record ReportColumnDefinition(
    string Field,
    string Label,
    string DataType, // string, number, date, boolean, enum
    bool IsFilterable,
    bool IsGroupable,
    bool IsSortable,
    List<string>? EnumValues,
    string? Description
);

// =============================================================================
// SHARED DTOs
// =============================================================================

public record ReportPaginationDto(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages
);
