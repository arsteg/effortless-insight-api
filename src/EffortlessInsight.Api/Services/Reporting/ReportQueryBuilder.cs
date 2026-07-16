using System.Linq.Expressions;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Builds and executes dynamic queries for reports
/// </summary>
public class ReportQueryBuilder : IReportQueryBuilder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ReportQueryBuilder> _logger;

    public ReportQueryBuilder(
        ApplicationDbContext dbContext,
        ILogger<ReportQueryBuilder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ReportResultDto> BuildAndExecuteAsync(
        Guid organizationId,
        string reportType,
        ReportConfiguration configuration,
        int page = 1,
        int pageSize = 50)
    {
        return reportType switch
        {
            ReportTypes.Notices => await ExecuteNoticesReportAsync(organizationId, configuration, page, pageSize),
            ReportTypes.Tasks => await ExecuteTasksReportAsync(organizationId, configuration, page, pageSize),
            ReportTypes.Users => await ExecuteUsersReportAsync(organizationId, configuration, page, pageSize),
            ReportTypes.Compliance => await ExecuteComplianceReportAsync(organizationId, configuration, page, pageSize),
            _ => throw new InvalidOperationException($"UNSUPPORTED_REPORT_TYPE: {reportType}")
        };
    }

    // ==========================================================================
    // Notices Report
    // ==========================================================================

    private async Task<ReportResultDto> ExecuteNoticesReportAsync(
        Guid organizationId,
        ReportConfiguration configuration,
        int page,
        int pageSize)
    {
        var query = _dbContext.Notices
            .Include(n => n.AssignedTo)
            .Where(n => n.OrganizationId == organizationId && n.DeletedAt == null);

        // Apply date range filter
        query = ApplyDateRangeFilter(query, configuration.DateRange, n => n.CreatedAt);

        // Apply custom filters
        query = ApplyNoticeFilters(query, configuration.Filters);

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = ApplyNoticeSorting(query, configuration.SortBy, configuration.SortDescending);

        // Apply limit or pagination
        var effectivePageSize = configuration.Limit.HasValue
            ? Math.Min(configuration.Limit.Value, pageSize)
            : pageSize;

        var notices = await query
            .Skip((page - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync();

        // Build result rows based on selected columns
        var rows = notices.Select(n => BuildNoticeRow(n, configuration.Columns)).ToList();

        // Build summary if requested
        ReportSummaryDto? summary = null;
        if (configuration.IncludeSummary)
        {
            summary = await BuildNoticeSummaryAsync(organizationId, configuration);
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)effectivePageSize);

        return new ReportResultDto(
            Columns: configuration.Columns,
            Rows: rows,
            Summary: summary,
            Pagination: new ReportPaginationDto(page, effectivePageSize, totalCount, totalPages)
        );
    }

    private IQueryable<Notice> ApplyNoticeFilters(
        IQueryable<Notice> query,
        List<ReportFilter> filters)
    {
        foreach (var filter in filters)
        {
            query = filter.Field.ToLowerInvariant() switch
            {
                "noticetype" => ApplyStringFilter(query, n => n.NoticeType, filter),
                "noticecategory" => ApplyStringFilter(query, n => n.NoticeCategory, filter),
                "status" => ApplyStringFilter(query, n => n.Status, filter),
                "priority" => ApplyStringFilter(query, n => n.Priority, filter),
                "gstin" => ApplyStringFilter(query, n => n.Gstin, filter),
                "financialyear" => ApplyStringFilter(query, n => n.FinancialYear, filter),
                "issuingauthority" => ApplyStringFilter(query, n => n.IssuingAuthority, filter),
                "assignedto" => filter.Operator == FilterOperators.IsNull
                    ? query.Where(n => n.AssignedToId == null)
                    : filter.Operator == FilterOperators.IsNotNull
                        ? query.Where(n => n.AssignedToId != null)
                        : query,
                "taxamount" => ApplyDecimalFilter(query, n => n.TaxAmount, filter),
                "penaltyamount" => ApplyDecimalFilter(query, n => n.PenaltyAmount, filter),
                "interestamount" => ApplyDecimalFilter(query, n => n.InterestAmount, filter),
                "totaldemand" => ApplyDecimalFilter(query, n => n.TotalDemand, filter),
                "issuedate" => ApplyDateOnlyFilter(query, n => n.IssueDate, filter),
                "responsedeadline" => ApplyDateOnlyFilter(query, n => n.ResponseDeadline, filter),
                _ => query
            };
        }

        return query;
    }

    private IQueryable<Notice> ApplyNoticeSorting(
        IQueryable<Notice> query,
        string? sortBy,
        bool descending)
    {
        if (string.IsNullOrEmpty(sortBy))
        {
            return descending
                ? query.OrderByDescending(n => n.CreatedAt)
                : query.OrderBy(n => n.CreatedAt);
        }

        return sortBy.ToLowerInvariant() switch
        {
            "noticenumber" => descending ? query.OrderByDescending(n => n.NoticeNumber) : query.OrderBy(n => n.NoticeNumber),
            "noticetype" => descending ? query.OrderByDescending(n => n.NoticeType) : query.OrderBy(n => n.NoticeType),
            "status" => descending ? query.OrderByDescending(n => n.Status) : query.OrderBy(n => n.Status),
            "priority" => descending ? query.OrderByDescending(n => n.Priority) : query.OrderBy(n => n.Priority),
            "issuedate" => descending ? query.OrderByDescending(n => n.IssueDate) : query.OrderBy(n => n.IssueDate),
            "responsedeadline" => descending ? query.OrderByDescending(n => n.ResponseDeadline) : query.OrderBy(n => n.ResponseDeadline),
            "taxamount" => descending ? query.OrderByDescending(n => n.TaxAmount) : query.OrderBy(n => n.TaxAmount),
            "totaldemand" => descending ? query.OrderByDescending(n => n.TotalDemand) : query.OrderBy(n => n.TotalDemand),
            "createdat" => descending ? query.OrderByDescending(n => n.CreatedAt) : query.OrderBy(n => n.CreatedAt),
            _ => descending ? query.OrderByDescending(n => n.CreatedAt) : query.OrderBy(n => n.CreatedAt)
        };
    }

    private static Dictionary<string, object?> BuildNoticeRow(Notice notice, List<string> columns)
    {
        var row = new Dictionary<string, object?>();

        foreach (var column in columns)
        {
            row[column] = column.ToLowerInvariant() switch
            {
                "noticenumber" => notice.NoticeNumber,
                "noticetype" => notice.NoticeType,
                "noticecategory" => notice.NoticeCategory,
                "gstin" => notice.Gstin,
                "status" => notice.Status,
                "priority" => notice.Priority,
                "issuedate" => notice.IssueDate?.ToString("yyyy-MM-dd"),
                "responsedeadline" => notice.ResponseDeadline?.ToString("yyyy-MM-dd"),
                "taxamount" => notice.TaxAmount,
                "penaltyamount" => notice.PenaltyAmount,
                "interestamount" => notice.InterestAmount,
                "totaldemand" => notice.TotalDemand,
                "assignedto" => notice.AssignedTo?.Name,
                "issuingauthority" => notice.IssuingAuthority,
                "financialyear" => notice.FinancialYear,
                "createdat" => notice.CreatedAt.ToString("yyyy-MM-dd"),
                _ => null
            };
        }

        return row;
    }

    private async Task<ReportSummaryDto> BuildNoticeSummaryAsync(
        Guid organizationId,
        ReportConfiguration configuration)
    {
        var query = _dbContext.Notices
            .Where(n => n.OrganizationId == organizationId && n.DeletedAt == null);

        query = ApplyDateRangeFilter(query, configuration.DateRange, n => n.CreatedAt);
        query = ApplyNoticeFilters(query, configuration.Filters);

        var aggregations = await query
            .GroupBy(n => 1)
            .Select(g => new
            {
                TotalCount = g.Count(),
                TotalTaxAmount = g.Sum(n => n.TaxAmount ?? 0),
                TotalPenaltyAmount = g.Sum(n => n.PenaltyAmount ?? 0),
                TotalInterestAmount = g.Sum(n => n.InterestAmount ?? 0),
                TotalDemand = g.Sum(n => n.TotalDemand ?? 0),
                OverdueCount = g.Count(n => n.ResponseDeadline < DateOnly.FromDateTime(DateTime.UtcNow) &&
                                          n.Status != NoticeStatus.Closed && n.Status != NoticeStatus.Responded),
                HighPriorityCount = g.Count(n => n.Priority == NoticePriority.High || n.Priority == NoticePriority.Critical)
            })
            .FirstOrDefaultAsync();

        return new ReportSummaryDto(
            TotalCount: aggregations?.TotalCount ?? 0,
            Aggregations: new Dictionary<string, object?>
            {
                ["totalTaxAmount"] = aggregations?.TotalTaxAmount ?? 0,
                ["totalPenaltyAmount"] = aggregations?.TotalPenaltyAmount ?? 0,
                ["totalInterestAmount"] = aggregations?.TotalInterestAmount ?? 0,
                ["totalDemand"] = aggregations?.TotalDemand ?? 0,
                ["overdueCount"] = aggregations?.OverdueCount ?? 0,
                ["highPriorityCount"] = aggregations?.HighPriorityCount ?? 0
            }
        );
    }

    // ==========================================================================
    // Tasks Report
    // ==========================================================================

    private async Task<ReportResultDto> ExecuteTasksReportAsync(
        Guid organizationId,
        ReportConfiguration configuration,
        int page,
        int pageSize)
    {
        var query = _dbContext.Tasks
            .Include(t => t.Notice)
            .Include(t => t.Assignees).ThenInclude(a => a.User)
            .Where(t => t.Notice.OrganizationId == organizationId && t.DeletedAt == null);

        // Apply date range filter
        query = ApplyDateRangeFilter(query, configuration.DateRange, t => t.CreatedAt);

        // Apply custom filters
        query = ApplyTaskFilters(query, configuration.Filters);

        var totalCount = await query.CountAsync();

        // Apply sorting
        query = ApplyTaskSorting(query, configuration.SortBy, configuration.SortDescending);

        var effectivePageSize = configuration.Limit.HasValue
            ? Math.Min(configuration.Limit.Value, pageSize)
            : pageSize;

        var tasks = await query
            .Skip((page - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync();

        var rows = tasks.Select(t => BuildTaskRow(t, configuration.Columns)).ToList();

        ReportSummaryDto? summary = null;
        if (configuration.IncludeSummary)
        {
            summary = await BuildTaskSummaryAsync(organizationId, configuration);
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)effectivePageSize);

        return new ReportResultDto(
            Columns: configuration.Columns,
            Rows: rows,
            Summary: summary,
            Pagination: new ReportPaginationDto(page, effectivePageSize, totalCount, totalPages)
        );
    }

    private IQueryable<NoticeTask> ApplyTaskFilters(
        IQueryable<NoticeTask> query,
        List<ReportFilter> filters)
    {
        foreach (var filter in filters)
        {
            query = filter.Field.ToLowerInvariant() switch
            {
                "status" => ApplyStringFilter(query, t => t.Status, filter),
                "priority" => ApplyStringFilter(query, t => t.Priority, filter),
                "noticetype" => ApplyStringFilter(query, t => t.Notice.NoticeType, filter),
                "isoverdue" => filter.Value is true
                    ? query.Where(t => t.DueDate < DateTime.UtcNow && t.Status != "done")
                    : query.Where(t => t.DueDate >= DateTime.UtcNow || t.Status == "done"),
                "duedate" => ApplyDateTimeFilter(query, t => t.DueDate, filter),
                _ => query
            };
        }

        return query;
    }

    private IQueryable<NoticeTask> ApplyTaskSorting(
        IQueryable<NoticeTask> query,
        string? sortBy,
        bool descending)
    {
        if (string.IsNullOrEmpty(sortBy))
        {
            return descending
                ? query.OrderByDescending(t => t.CreatedAt)
                : query.OrderBy(t => t.CreatedAt);
        }

        return sortBy.ToLowerInvariant() switch
        {
            "title" => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            "status" => descending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "priority" => descending ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
            "duedate" => descending ? query.OrderByDescending(t => t.DueDate) : query.OrderBy(t => t.DueDate),
            "completedat" => descending ? query.OrderByDescending(t => t.CompletedAt) : query.OrderBy(t => t.CompletedAt),
            "createdat" => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
            _ => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt)
        };
    }

    private static Dictionary<string, object?> BuildTaskRow(NoticeTask task, List<string> columns)
    {
        var row = new Dictionary<string, object?>();

        foreach (var column in columns)
        {
            row[column] = column.ToLowerInvariant() switch
            {
                "title" => task.Title,
                "status" => task.Status,
                "priority" => task.Priority,
                "duedate" => task.DueDate?.ToString("yyyy-MM-dd"),
                "assignee" => string.Join(", ", task.Assignees.Select(a => a.User.Name)),
                "noticenumber" => task.Notice.NoticeNumber,
                "noticetype" => task.Notice.NoticeType,
                "estimatedhours" => task.EstimatedHours,
                "actualhours" => task.ActualHours,
                "isoverdue" => task.DueDate.HasValue && task.DueDate < DateTime.UtcNow && task.Status != "done",
                "completedat" => task.CompletedAt?.ToString("yyyy-MM-dd"),
                "createdat" => task.CreatedAt.ToString("yyyy-MM-dd"),
                _ => null
            };
        }

        return row;
    }

    private async Task<ReportSummaryDto> BuildTaskSummaryAsync(
        Guid organizationId,
        ReportConfiguration configuration)
    {
        var query = _dbContext.Tasks
            .Include(t => t.Notice)
            .Where(t => t.Notice.OrganizationId == organizationId && t.DeletedAt == null);

        query = ApplyDateRangeFilter(query, configuration.DateRange, t => t.CreatedAt);
        query = ApplyTaskFilters(query, configuration.Filters);

        var now = DateTime.UtcNow;

        var aggregations = await query
            .GroupBy(t => 1)
            .Select(g => new
            {
                TotalCount = g.Count(),
                TodoCount = g.Count(t => t.Status == "todo"),
                InProgressCount = g.Count(t => t.Status == "in_progress"),
                DoneCount = g.Count(t => t.Status == "done"),
                BlockedCount = g.Count(t => t.Status == "blocked"),
                OverdueCount = g.Count(t => t.DueDate < now && t.Status != "done"),
                TotalEstimatedHours = g.Sum(t => t.EstimatedHours ?? 0),
                TotalActualHours = g.Sum(t => t.ActualHours ?? 0)
            })
            .FirstOrDefaultAsync();

        return new ReportSummaryDto(
            TotalCount: aggregations?.TotalCount ?? 0,
            Aggregations: new Dictionary<string, object?>
            {
                ["todoCount"] = aggregations?.TodoCount ?? 0,
                ["inProgressCount"] = aggregations?.InProgressCount ?? 0,
                ["doneCount"] = aggregations?.DoneCount ?? 0,
                ["blockedCount"] = aggregations?.BlockedCount ?? 0,
                ["overdueCount"] = aggregations?.OverdueCount ?? 0,
                ["totalEstimatedHours"] = aggregations?.TotalEstimatedHours ?? 0,
                ["totalActualHours"] = aggregations?.TotalActualHours ?? 0
            }
        );
    }

    // ==========================================================================
    // Users Report
    // ==========================================================================

    private async Task<ReportResultDto> ExecuteUsersReportAsync(
        Guid organizationId,
        ReportConfiguration configuration,
        int page,
        int pageSize)
    {
        var query = _dbContext.OrganizationMembers
            .Include(m => m.User)
            .Where(m => m.OrganizationId == organizationId && m.DeletedAt == null);

        var totalCount = await query.CountAsync();

        var effectivePageSize = configuration.Limit.HasValue
            ? Math.Min(configuration.Limit.Value, pageSize)
            : pageSize;

        var members = await query
            .Skip((page - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync();

        // Get task counts for each user
        var userIds = members.Select(m => m.UserId).ToList();
        var taskCounts = await _dbContext.TaskAssignees
            .Include(ta => ta.Task).ThenInclude(t => t.Notice)
            .Where(ta => userIds.Contains(ta.UserId) &&
                        ta.Task.Notice.OrganizationId == organizationId &&
                        ta.Task.DeletedAt == null)
            .GroupBy(ta => ta.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Assigned = g.Count(),
                Completed = g.Count(ta => ta.Task.Status == "done")
            })
            .ToListAsync();

        var noticeCounts = await _dbContext.Notices
            .Where(n => n.OrganizationId == organizationId &&
                       n.DeletedAt == null &&
                       n.AssignedToId != null &&
                       userIds.Contains(n.AssignedToId.Value))
            .GroupBy(n => n.AssignedToId)
            .Select(g => new
            {
                UserId = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        var rows = members.Select(m =>
        {
            var taskCount = taskCounts.FirstOrDefault(tc => tc.UserId == m.UserId);
            var noticeCount = noticeCounts.FirstOrDefault(nc => nc.UserId == m.UserId);

            return BuildUserRow(m, taskCount?.Assigned ?? 0, taskCount?.Completed ?? 0, noticeCount?.Count ?? 0, configuration.Columns);
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)effectivePageSize);

        ReportSummaryDto? summary = null;
        if (configuration.IncludeSummary)
        {
            summary = new ReportSummaryDto(
                TotalCount: totalCount,
                Aggregations: new Dictionary<string, object?>
                {
                    ["activeCount"] = members.Count(m => m.Status == "active"),
                    ["inactiveCount"] = members.Count(m => m.Status == "inactive"),
                    ["suspendedCount"] = members.Count(m => m.Status == "suspended")
                }
            );
        }

        return new ReportResultDto(
            Columns: configuration.Columns,
            Rows: rows,
            Summary: summary,
            Pagination: new ReportPaginationDto(page, effectivePageSize, totalCount, totalPages)
        );
    }

    private static Dictionary<string, object?> BuildUserRow(
        OrganizationMember member,
        int tasksAssigned,
        int tasksCompleted,
        int noticesAssigned,
        List<string> columns)
    {
        var row = new Dictionary<string, object?>();

        foreach (var column in columns)
        {
            row[column] = column.ToLowerInvariant() switch
            {
                "name" => member.User.Name,
                "email" => member.User.Email,
                "role" => member.Role,
                "status" => member.Status,
                "tasksassigned" => tasksAssigned,
                "taskscompleted" => tasksCompleted,
                "noticesassigned" => noticesAssigned,
                "lastactiveat" => member.User.LastActivityAt?.ToString("yyyy-MM-dd"),
                "joinedat" => member.JoinedAt.ToString("yyyy-MM-dd"),
                _ => null
            };
        }

        return row;
    }

    // ==========================================================================
    // Compliance Report
    // ==========================================================================

    private async Task<ReportResultDto> ExecuteComplianceReportAsync(
        Guid organizationId,
        ReportConfiguration configuration,
        int page,
        int pageSize)
    {
        // Group notices by GSTIN for compliance metrics
        var now = DateOnly.FromDateTime(DateTime.UtcNow);

        var gstinMetrics = await _dbContext.Notices
            .Where(n => n.OrganizationId == organizationId &&
                       n.DeletedAt == null &&
                       n.Gstin != null)
            .GroupBy(n => n.Gstin)
            .Select(g => new
            {
                Gstin = g.Key,
                TotalNotices = g.Count(),
                OpenNotices = g.Count(n => n.Status != NoticeStatus.Closed && n.Status != NoticeStatus.Responded),
                ClosedNotices = g.Count(n => n.Status == NoticeStatus.Closed || n.Status == NoticeStatus.Responded),
                OverdueNotices = g.Count(n => n.ResponseDeadline < now &&
                                             n.Status != NoticeStatus.Closed && n.Status != NoticeStatus.Responded),
                TotalDemand = g.Sum(n => n.TotalDemand ?? 0),
                LastNoticeDate = g.Max(n => n.IssueDate)
            })
            .ToListAsync();

        var totalCount = gstinMetrics.Count;
        var effectivePageSize = configuration.Limit.HasValue
            ? Math.Min(configuration.Limit.Value, pageSize)
            : pageSize;

        var pagedMetrics = gstinMetrics
            .Skip((page - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToList();

        var rows = pagedMetrics.Select(m =>
        {
            var complianceScore = CalculateComplianceScore(m.TotalNotices, m.OpenNotices, m.OverdueNotices);

            var row = new Dictionary<string, object?>();
            foreach (var column in configuration.Columns)
            {
                row[column] = column.ToLowerInvariant() switch
                {
                    "gstin" => m.Gstin,
                    "totalnotices" => m.TotalNotices,
                    "opennotices" => m.OpenNotices,
                    "closednotices" => m.ClosedNotices,
                    "overduenotices" => m.OverdueNotices,
                    "totaldemand" => m.TotalDemand,
                    "compliancescore" => complianceScore,
                    "lastnoticedate" => m.LastNoticeDate?.ToString("yyyy-MM-dd"),
                    _ => null
                };
            }
            return row;
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)effectivePageSize);

        ReportSummaryDto? summary = null;
        if (configuration.IncludeSummary)
        {
            summary = new ReportSummaryDto(
                TotalCount: totalCount,
                Aggregations: new Dictionary<string, object?>
                {
                    ["totalNoticesAllGstins"] = gstinMetrics.Sum(m => m.TotalNotices),
                    ["totalOpenNotices"] = gstinMetrics.Sum(m => m.OpenNotices),
                    ["totalOverdueNotices"] = gstinMetrics.Sum(m => m.OverdueNotices),
                    ["totalDemandAllGstins"] = gstinMetrics.Sum(m => m.TotalDemand),
                    ["avgComplianceScore"] = gstinMetrics.Count > 0
                        ? gstinMetrics.Average(m => CalculateComplianceScore(m.TotalNotices, m.OpenNotices, m.OverdueNotices))
                        : 100
                }
            );
        }

        return new ReportResultDto(
            Columns: configuration.Columns,
            Rows: rows,
            Summary: summary,
            Pagination: new ReportPaginationDto(page, effectivePageSize, totalCount, totalPages)
        );
    }

    private static int CalculateComplianceScore(int total, int open, int overdue)
    {
        if (total == 0) return 100;

        var closedRate = (total - open) / (double)total;
        var overdueRate = overdue / (double)total;

        // Score: 70% based on closure rate, 30% penalty for overdue
        var score = (closedRate * 70) + ((1 - overdueRate) * 30);
        return (int)Math.Round(score);
    }

    // ==========================================================================
    // Common Filter Helpers
    // ==========================================================================

    private static IQueryable<T> ApplyDateRangeFilter<T>(
        IQueryable<T> query,
        DateRangeConfig? dateRange,
        Expression<Func<T, DateTime>> dateSelector)
    {
        if (dateRange == null) return query;

        var (startDate, endDate) = GetDateRange(dateRange);

        if (startDate.HasValue)
        {
            var start = DateTime.SpecifyKind(startDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var param = dateSelector.Parameters[0];
            var body = Expression.GreaterThanOrEqual(dateSelector.Body, Expression.Constant(start));
            var lambda = Expression.Lambda<Func<T, bool>>(body, param);
            query = query.Where(lambda);
        }

        if (endDate.HasValue)
        {
            var end = DateTime.SpecifyKind(endDate.Value.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
            var param = dateSelector.Parameters[0];
            var body = Expression.LessThanOrEqual(dateSelector.Body, Expression.Constant(end));
            var lambda = Expression.Lambda<Func<T, bool>>(body, param);
            query = query.Where(lambda);
        }

        return query;
    }

    private static (DateOnly? Start, DateOnly? End) GetDateRange(DateRangeConfig config)
    {
        if (config.Preset == DateRangePresets.Custom)
        {
            return (config.StartDate, config.EndDate);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return config.Preset switch
        {
            DateRangePresets.Today => (today, today),
            DateRangePresets.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            DateRangePresets.ThisWeek => (today.AddDays(-(int)today.DayOfWeek), today),
            DateRangePresets.LastWeek => (today.AddDays(-(int)today.DayOfWeek - 7), today.AddDays(-(int)today.DayOfWeek - 1)),
            DateRangePresets.ThisMonth => (new DateOnly(today.Year, today.Month, 1), today),
            DateRangePresets.LastMonth => (new DateOnly(today.Year, today.Month, 1).AddMonths(-1),
                                           new DateOnly(today.Year, today.Month, 1).AddDays(-1)),
            DateRangePresets.ThisQuarter => GetQuarterDates(today, 0),
            DateRangePresets.LastQuarter => GetQuarterDates(today, -1),
            DateRangePresets.ThisYear => (new DateOnly(today.Year, 1, 1), today),
            DateRangePresets.LastYear => (new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31)),
            DateRangePresets.Last7Days => (today.AddDays(-7), today),
            DateRangePresets.Last30Days => (today.AddDays(-30), today),
            DateRangePresets.Last90Days => (today.AddDays(-90), today),
            _ => (null, null)
        };
    }

    private static (DateOnly Start, DateOnly End) GetQuarterDates(DateOnly date, int quarterOffset)
    {
        var quarter = (date.Month - 1) / 3 + quarterOffset;
        var year = date.Year;

        while (quarter < 0)
        {
            quarter += 4;
            year--;
        }
        while (quarter > 3)
        {
            quarter -= 4;
            year++;
        }

        var startMonth = quarter * 3 + 1;
        var start = new DateOnly(year, startMonth, 1);
        var end = start.AddMonths(3).AddDays(-1);

        return (start, end);
    }

    private static IQueryable<T> ApplyStringFilter<T>(
        IQueryable<T> query,
        Expression<Func<T, string?>> selector,
        ReportFilter filter)
    {
        var value = filter.Value?.ToString();
        if (string.IsNullOrEmpty(value) && filter.Operator != FilterOperators.IsNull && filter.Operator != FilterOperators.IsNotNull)
            return query;

        var param = selector.Parameters[0];
        Expression body = filter.Operator switch
        {
            FilterOperators.Equal => Expression.Equal(selector.Body, Expression.Constant(value)),
            FilterOperators.NotEqual => Expression.NotEqual(selector.Body, Expression.Constant(value)),
            FilterOperators.Contains => Expression.Call(
                selector.Body,
                typeof(string).GetMethod("Contains", [typeof(string)])!,
                Expression.Constant(value ?? "")),
            FilterOperators.IsNull => Expression.Equal(selector.Body, Expression.Constant(null, typeof(string))),
            FilterOperators.IsNotNull => Expression.NotEqual(selector.Body, Expression.Constant(null, typeof(string))),
            _ => Expression.Constant(true)
        };

        var lambda = Expression.Lambda<Func<T, bool>>(body, param);
        return query.Where(lambda);
    }

    private static IQueryable<T> ApplyDecimalFilter<T>(
        IQueryable<T> query,
        Expression<Func<T, decimal?>> selector,
        ReportFilter filter)
    {
        if (!decimal.TryParse(filter.Value?.ToString(), out var value))
            return query;

        var param = selector.Parameters[0];
        Expression body = filter.Operator switch
        {
            FilterOperators.Equal => Expression.Equal(selector.Body, Expression.Constant((decimal?)value)),
            FilterOperators.NotEqual => Expression.NotEqual(selector.Body, Expression.Constant((decimal?)value)),
            FilterOperators.GreaterThan => Expression.GreaterThan(selector.Body, Expression.Constant((decimal?)value)),
            FilterOperators.LessThan => Expression.LessThan(selector.Body, Expression.Constant((decimal?)value)),
            FilterOperators.GreaterThanOrEqual => Expression.GreaterThanOrEqual(selector.Body, Expression.Constant((decimal?)value)),
            FilterOperators.LessThanOrEqual => Expression.LessThanOrEqual(selector.Body, Expression.Constant((decimal?)value)),
            _ => Expression.Constant(true)
        };

        var lambda = Expression.Lambda<Func<T, bool>>(body, param);
        return query.Where(lambda);
    }

    private static IQueryable<T> ApplyDateOnlyFilter<T>(
        IQueryable<T> query,
        Expression<Func<T, DateOnly?>> selector,
        ReportFilter filter)
    {
        if (!DateOnly.TryParse(filter.Value?.ToString(), out var value))
            return query;

        var param = selector.Parameters[0];
        Expression body = filter.Operator switch
        {
            FilterOperators.Equal => Expression.Equal(selector.Body, Expression.Constant((DateOnly?)value)),
            FilterOperators.NotEqual => Expression.NotEqual(selector.Body, Expression.Constant((DateOnly?)value)),
            FilterOperators.GreaterThan => Expression.GreaterThan(selector.Body, Expression.Constant((DateOnly?)value)),
            FilterOperators.LessThan => Expression.LessThan(selector.Body, Expression.Constant((DateOnly?)value)),
            FilterOperators.GreaterThanOrEqual => Expression.GreaterThanOrEqual(selector.Body, Expression.Constant((DateOnly?)value)),
            FilterOperators.LessThanOrEqual => Expression.LessThanOrEqual(selector.Body, Expression.Constant((DateOnly?)value)),
            _ => Expression.Constant(true)
        };

        var lambda = Expression.Lambda<Func<T, bool>>(body, param);
        return query.Where(lambda);
    }

    private static IQueryable<T> ApplyDateTimeFilter<T>(
        IQueryable<T> query,
        Expression<Func<T, DateTime?>> selector,
        ReportFilter filter)
    {
        if (!DateTime.TryParse(filter.Value?.ToString(), out var value))
            return query;

        var param = selector.Parameters[0];
        Expression body = filter.Operator switch
        {
            FilterOperators.Equal => Expression.Equal(selector.Body, Expression.Constant((DateTime?)value)),
            FilterOperators.NotEqual => Expression.NotEqual(selector.Body, Expression.Constant((DateTime?)value)),
            FilterOperators.GreaterThan => Expression.GreaterThan(selector.Body, Expression.Constant((DateTime?)value)),
            FilterOperators.LessThan => Expression.LessThan(selector.Body, Expression.Constant((DateTime?)value)),
            FilterOperators.GreaterThanOrEqual => Expression.GreaterThanOrEqual(selector.Body, Expression.Constant((DateTime?)value)),
            FilterOperators.LessThanOrEqual => Expression.LessThanOrEqual(selector.Body, Expression.Constant((DateTime?)value)),
            _ => Expression.Constant(true)
        };

        var lambda = Expression.Lambda<Func<T, bool>>(body, param);
        return query.Where(lambda);
    }
}
