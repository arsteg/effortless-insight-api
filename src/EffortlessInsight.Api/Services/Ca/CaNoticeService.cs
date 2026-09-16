using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for CA notice access.
/// </summary>
public class CaNoticeService : ICaNoticeService
{
    private readonly ApplicationDbContext _db;
    private readonly ICaAuthorizationService _authService;
    private readonly ILogger<CaNoticeService> _logger;

    public CaNoticeService(
        ApplicationDbContext db,
        ICaAuthorizationService authService,
        ILogger<CaNoticeService> logger)
    {
        _db = db;
        _authService = authService;
        _logger = logger;
    }

    public async Task<CaNoticeListResponse> GetNoticesAsync(
        Guid caUserId,
        Guid organizationId,
        List<string> authorizedGstins,
        CaNoticeFilterDto filter,
        CancellationToken ct = default)
    {
        // Query notices from client's organization for authorized GSTINs
        var query = _db.Notices
            .IgnoreQueryFilters()
            .Include(n => n.GstinNavigation)
            .Where(n =>
                n.OrganizationId == organizationId &&
                n.Gstin != null &&
                authorizedGstins.Contains(n.Gstin) &&
                n.DeletedAt == null &&
                !n.IsStaged);

        // Apply filters
        if (!string.IsNullOrEmpty(filter.Gstin))
        {
            query = query.Where(n => n.Gstin == filter.Gstin);
        }

        if (!string.IsNullOrEmpty(filter.Status))
        {
            query = query.Where(n => n.Status == filter.Status);
        }

        if (!string.IsNullOrEmpty(filter.Priority))
        {
            query = query.Where(n => n.Priority == filter.Priority);
        }

        if (!string.IsNullOrEmpty(filter.NoticeType))
        {
            query = query.Where(n => n.NoticeType == filter.NoticeType);
        }

        if (filter.IssueDateFrom.HasValue)
        {
            query = query.Where(n => n.IssueDate >= filter.IssueDateFrom.Value);
        }

        if (filter.IssueDateTo.HasValue)
        {
            query = query.Where(n => n.IssueDate <= filter.IssueDateTo.Value);
        }

        if (filter.DeadlineFrom.HasValue)
        {
            query = query.Where(n => n.ResponseDeadline >= filter.DeadlineFrom.Value);
        }

        if (filter.DeadlineTo.HasValue)
        {
            query = query.Where(n => n.ResponseDeadline <= filter.DeadlineTo.Value);
        }

        if (!string.IsNullOrEmpty(filter.SearchTerm))
        {
            var term = filter.SearchTerm.ToLower();
            query = query.Where(n =>
                (n.NoticeNumber != null && n.NoticeNumber.ToLower().Contains(term)) ||
                (n.Summary != null && n.Summary.ToLower().Contains(term)) ||
                (n.NoticeType != null && n.NoticeType.ToLower().Contains(term)));
        }

        // Get total count
        var total = await query.CountAsync(ct);

        // Apply sorting
        query = filter.SortBy.ToLower() switch
        {
            "issuedate" => filter.SortDescending
                ? query.OrderByDescending(n => n.IssueDate)
                : query.OrderBy(n => n.IssueDate),
            "responsedeadline" => filter.SortDescending
                ? query.OrderByDescending(n => n.ResponseDeadline)
                : query.OrderBy(n => n.ResponseDeadline),
            "totaldemand" => filter.SortDescending
                ? query.OrderByDescending(n => n.TotalDemand)
                : query.OrderBy(n => n.TotalDemand),
            "status" => filter.SortDescending
                ? query.OrderByDescending(n => n.Status)
                : query.OrderBy(n => n.Status),
            _ => filter.SortDescending
                ? query.OrderByDescending(n => n.CreatedAt)
                : query.OrderBy(n => n.CreatedAt)
        };

        // Apply pagination
        var notices = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var items = notices.Select(n => MapToDto(n, caUserId)).ToList();

        return new CaNoticeListResponse(
            Items: items,
            Total: total,
            Page: filter.Page,
            PageSize: filter.PageSize,
            TotalPages: (int)Math.Ceiling((double)total / filter.PageSize)
        );
    }

    public async Task<CaNoticeDetailDto?> GetNoticeAsync(
        Guid caUserId,
        Guid noticeId,
        Guid organizationId,
        List<string> authorizedGstins,
        CancellationToken ct = default)
    {
        var notice = await _db.Notices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.Gstin != null &&
                authorizedGstins.Contains(n.Gstin) &&
                n.DeletedAt == null,
                ct);

        if (notice == null)
            return null;

        // Check permissions for this GSTIN
        var canComment = await _authService.HasAccessAsync(caUserId, notice.Gstin!, CaPermission.Comment, ct);
        var canDraftResponse = await _authService.HasAccessAsync(caUserId, notice.Gstin!, CaPermission.DraftResponse, ct);

        return new CaNoticeDetailDto(
            Id: notice.Id,
            NoticeNumber: notice.NoticeNumber,
            NoticeType: notice.NoticeType,
            NoticeCategory: notice.NoticeCategory,
            NoticeSubCategory: notice.NoticeSubCategory,
            Gstin: notice.Gstin!,
            TradeName: notice.GstinNavigation?.TradeName,
            IssueDate: notice.IssueDate,
            ResponseDeadline: notice.ResponseDeadline,
            ExtendedDeadline: notice.ExtendedDeadline,
            HearingDate: notice.HearingDate,
            TaxAmount: notice.TaxAmount,
            PenaltyAmount: notice.PenaltyAmount,
            InterestAmount: notice.InterestAmount,
            TotalDemand: notice.TotalDemand,
            PeriodFrom: notice.PeriodFrom,
            PeriodTo: notice.PeriodTo,
            FinancialYear: notice.FinancialYear,
            IssuingAuthority: notice.IssuingAuthority,
            IssuingOfficer: notice.IssuingOfficer,
            OfficerDesignation: notice.OfficerDesignation,
            Jurisdiction: notice.Jurisdiction,
            Status: notice.Status,
            Priority: notice.Priority,
            Summary: notice.Summary,
            Section: notice.Section,
            FileUrl: notice.FileUrl,
            FileName: notice.FileName,
            Source: notice.Source,
            Tags: notice.Tags,
            CreatedAt: notice.CreatedAt,
            UpdatedAt: notice.UpdatedAt,
            CanComment: canComment,
            CanDraftResponse: canDraftResponse
        );
    }

    public async Task<(int total, int pending, int overdue)> GetNoticeCountsAsync(
        Guid organizationId,
        List<string> gstins,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var total = await _db.Notices
            .IgnoreQueryFilters()
            .CountAsync(n =>
                n.OrganizationId == organizationId &&
                n.Gstin != null &&
                gstins.Contains(n.Gstin) &&
                n.DeletedAt == null &&
                !n.IsStaged,
                ct);

        var pendingStatuses = new[] { "uploaded", "processing", "analyzed", "in_progress" };
        var pending = await _db.Notices
            .IgnoreQueryFilters()
            .CountAsync(n =>
                n.OrganizationId == organizationId &&
                n.Gstin != null &&
                gstins.Contains(n.Gstin) &&
                n.DeletedAt == null &&
                !n.IsStaged &&
                pendingStatuses.Contains(n.Status),
                ct);

        var overdue = await _db.Notices
            .IgnoreQueryFilters()
            .CountAsync(n =>
                n.OrganizationId == organizationId &&
                n.Gstin != null &&
                gstins.Contains(n.Gstin) &&
                n.DeletedAt == null &&
                !n.IsStaged &&
                pendingStatuses.Contains(n.Status) &&
                n.ResponseDeadline != null &&
                n.ResponseDeadline < today,
                ct);

        return (total, pending, overdue);
    }

    private static CaNoticeDto MapToDto(Notice notice, Guid caUserId)
    {
        return new CaNoticeDto(
            Id: notice.Id,
            NoticeNumber: notice.NoticeNumber,
            NoticeType: notice.NoticeType,
            NoticeCategory: notice.NoticeCategory,
            Gstin: notice.Gstin!,
            TradeName: notice.GstinNavigation?.TradeName,
            IssueDate: notice.IssueDate,
            ResponseDeadline: notice.ResponseDeadline,
            TotalDemand: notice.TotalDemand,
            Status: notice.Status,
            Priority: notice.Priority,
            Summary: notice.Summary,
            IssuingAuthority: notice.IssuingAuthority,
            Source: notice.Source,
            CreatedAt: notice.CreatedAt,
            CaSyncedByUserId: notice.CaSyncedByUserId,
            IsStagedByMe: notice.CaSyncedByUserId == caUserId
        );
    }
}
