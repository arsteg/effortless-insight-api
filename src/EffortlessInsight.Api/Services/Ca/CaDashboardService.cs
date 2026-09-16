using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for CA dashboard data.
/// </summary>
public class CaDashboardService : ICaDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly ICaNoticeService _noticeService;
    private readonly ILogger<CaDashboardService> _logger;

    public CaDashboardService(
        ApplicationDbContext db,
        ICaNoticeService noticeService,
        ILogger<CaDashboardService> logger)
    {
        _db = db;
        _noticeService = noticeService;
        _logger = logger;
    }

    public async Task<CaDashboardDto> GetDashboardAsync(Guid caUserId, CancellationToken ct = default)
    {
        // Get client counts
        var totalClients = await _db.CaClientRelationships
            .CountAsync(r => r.CaUserId == caUserId, ct);

        var activeClients = await _db.CaClientRelationships
            .CountAsync(r =>
                r.CaUserId == caUserId &&
                r.Status == CaClientRelationshipStatus.Active,
                ct);

        var pendingInvitations = await _db.CaInvitations
            .CountAsync(i =>
                i.InviterUserId == caUserId &&
                i.Status == CaInvitationStatus.Pending,
                ct);

        // Get GSTIN counts
        var totalAuthorizedGstins = await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .CountAsync(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Status == CaGstinAuthorizationStatus.Active,
                ct);

        // Get notice counts across all clients
        var (totalNoticeCount, pendingNoticeCount) = await GetTotalNoticeCountsAsync(caUserId, ct);
        var noticesTodayCount = await GetNoticesTodayCountAsync(caUserId, ct);

        // Get recent clients
        var recentClients = await GetRecentClientsAsync(caUserId, 5, ct);

        // Get recent notices across all clients
        var recentNotices = await GetRecentNoticesAsync(caUserId, 10, ct);

        return new CaDashboardDto(
            TotalClients: totalClients,
            ActiveClients: activeClients,
            PendingInvitations: pendingInvitations,
            TotalAuthorizedGstins: totalAuthorizedGstins,
            TotalNoticeCount: totalNoticeCount,
            PendingNoticeCount: pendingNoticeCount,
            NoticesTodayCount: noticesTodayCount,
            RecentClients: recentClients,
            RecentNotices: recentNotices
        );
    }

    public async Task<CaClientDashboardDto> GetClientDashboardAsync(
        Guid caUserId,
        Guid relationshipId,
        CancellationToken ct = default)
    {
        var relationship = await _db.CaClientRelationships
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
                .ThenInclude(a => a.OrganizationGstin)
            .FirstOrDefaultAsync(r =>
                r.Id == relationshipId &&
                r.CaUserId == caUserId &&
                r.Status == CaClientRelationshipStatus.Active,
                ct);

        if (relationship == null)
        {
            throw new InvalidOperationException("Relationship not found or not active");
        }

        var authorizedGstins = relationship.GstinAuthorizations
            .Where(a => a.Status == CaGstinAuthorizationStatus.Active)
            .Select(a => a.Gstin)
            .ToList();

        // Get notice counts
        var (total, pending, overdue) = relationship.OrganizationId.HasValue
            ? await _noticeService.GetNoticeCountsAsync(relationship.OrganizationId.Value, authorizedGstins, ct)
            : (0, 0, 0);

        // Build authorization DTOs with notice counts
        var authDtos = new List<CaGstinAuthorizationDto>();
        foreach (var auth in relationship.GstinAuthorizations.Where(a => a.Status == CaGstinAuthorizationStatus.Active))
        {
            var noticeCount = relationship.OrganizationId.HasValue
                ? await GetNoticeCountForGstinAsync(relationship.OrganizationId.Value, auth.Gstin, ct)
                : 0;

            authDtos.Add(new CaGstinAuthorizationDto(
                Id: auth.Id,
                Gstin: auth.Gstin,
                OrganizationGstinId: auth.OrganizationGstinId,
                TradeName: auth.OrganizationGstin?.TradeName,
                LegalName: auth.OrganizationGstin?.LegalName,
                StateCode: auth.OrganizationGstin?.StateCode,
                StateName: auth.OrganizationGstin?.StateName,
                Status: auth.Status,
                Permissions: auth.Permissions,
                GrantedAt: auth.GrantedAt,
                LastSyncAt: auth.OrganizationGstin?.LastSyncedAt,
                NoticeCount: noticeCount
            ));
        }

        // Get recent notices for this client
        var recentNotices = relationship.OrganizationId.HasValue
            ? await GetRecentNoticesForClientAsync(relationship.OrganizationId.Value, authorizedGstins, 5, ct)
            : [];

        // Get recent activity
        var recentActivity = relationship.OrganizationId.HasValue
            ? await GetRecentActivityAsync(relationship.OrganizationId.Value, authorizedGstins, 10, ct)
            : [];

        return new CaClientDashboardDto(
            ClientRelationshipId: relationshipId,
            ClientName: relationship.ClientUser.Name,
            OrganizationName: relationship.Organization?.Name,
            AuthorizedGstins: authDtos,
            TotalNoticeCount: total,
            PendingNoticeCount: pending,
            OverdueNoticeCount: overdue,
            RecentNotices: recentNotices,
            RecentActivity: recentActivity
        );
    }

    private async Task<(int total, int pending)> GetTotalNoticeCountsAsync(Guid caUserId, CancellationToken ct)
    {
        // Get all active authorizations
        var authorizations = await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .Where(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Status == CaGstinAuthorizationStatus.Active &&
                a.CaClientRelationship.Status == CaClientRelationshipStatus.Active &&
                a.CaClientRelationship.OrganizationId != null)
            .ToListAsync(ct);

        int total = 0;
        int pending = 0;
        var pendingStatuses = new[] { "uploaded", "processing", "analyzed", "in_progress" };

        // Group by organization for efficient queries
        var byOrg = authorizations.GroupBy(a => a.CaClientRelationship.OrganizationId!.Value);
        foreach (var group in byOrg)
        {
            var gstins = group.Select(a => a.Gstin).Distinct().ToList();

            total += await _db.Notices
                .IgnoreQueryFilters()
                .CountAsync(n =>
                    n.OrganizationId == group.Key &&
                    n.Gstin != null &&
                    gstins.Contains(n.Gstin) &&
                    n.DeletedAt == null &&
                    !n.IsStaged,
                    ct);

            pending += await _db.Notices
                .IgnoreQueryFilters()
                .CountAsync(n =>
                    n.OrganizationId == group.Key &&
                    n.Gstin != null &&
                    gstins.Contains(n.Gstin) &&
                    n.DeletedAt == null &&
                    !n.IsStaged &&
                    pendingStatuses.Contains(n.Status),
                    ct);
        }

        return (total, pending);
    }

    private async Task<int> GetNoticesTodayCountAsync(Guid caUserId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var authorizations = await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .Where(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Status == CaGstinAuthorizationStatus.Active &&
                a.CaClientRelationship.Status == CaClientRelationshipStatus.Active &&
                a.CaClientRelationship.OrganizationId != null)
            .ToListAsync(ct);

        int count = 0;
        var byOrg = authorizations.GroupBy(a => a.CaClientRelationship.OrganizationId!.Value);
        foreach (var group in byOrg)
        {
            var gstins = group.Select(a => a.Gstin).Distinct().ToList();

            count += await _db.Notices
                .IgnoreQueryFilters()
                .CountAsync(n =>
                    n.OrganizationId == group.Key &&
                    n.Gstin != null &&
                    gstins.Contains(n.Gstin) &&
                    n.DeletedAt == null &&
                    !n.IsStaged &&
                    n.CreatedAt >= today,
                    ct);
        }

        return count;
    }

    private async Task<List<CaDashboardClientSummary>> GetRecentClientsAsync(
        Guid caUserId,
        int limit,
        CancellationToken ct)
    {
        var relationships = await _db.CaClientRelationships
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
            .Where(r =>
                r.CaUserId == caUserId &&
                r.Status == CaClientRelationshipStatus.Active)
            .OrderByDescending(r => r.AcceptedAt ?? r.InvitedAt)
            .Take(limit)
            .ToListAsync(ct);

        var result = new List<CaDashboardClientSummary>();
        foreach (var r in relationships)
        {
            var (total, pending, _) = r.OrganizationId.HasValue
                ? await _noticeService.GetNoticeCountsAsync(
                    r.OrganizationId.Value,
                    r.GstinAuthorizations.Where(a => a.Status == CaGstinAuthorizationStatus.Active).Select(a => a.Gstin).ToList(),
                    ct)
                : (0, 0, 0);

            result.Add(new CaDashboardClientSummary(
                RelationshipId: r.Id,
                ClientName: r.ClientUser.Name,
                OrganizationName: r.Organization?.Name,
                NoticeCount: total,
                PendingCount: pending,
                LastActivity: r.UpdatedAt ?? r.AcceptedAt
            ));
        }

        return result;
    }

    private async Task<List<CaDashboardNoticeSummary>> GetRecentNoticesAsync(
        Guid caUserId,
        int limit,
        CancellationToken ct)
    {
        // Get all active authorizations with client info
        var authorizations = await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
                .ThenInclude(r => r.ClientUser)
            .Where(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Status == CaGstinAuthorizationStatus.Active &&
                a.CaClientRelationship.Status == CaClientRelationshipStatus.Active &&
                a.CaClientRelationship.OrganizationId != null)
            .ToListAsync(ct);

        var result = new List<CaDashboardNoticeSummary>();

        // Query notices from each organization
        var byOrg = authorizations.GroupBy(a => new
        {
            OrgId = a.CaClientRelationship.OrganizationId!.Value,
            ClientName = a.CaClientRelationship.ClientUser.Name
        });

        foreach (var group in byOrg)
        {
            var gstins = group.Select(a => a.Gstin).Distinct().ToList();

            var notices = await _db.Notices
                .IgnoreQueryFilters()
                .Where(n =>
                    n.OrganizationId == group.Key.OrgId &&
                    n.Gstin != null &&
                    gstins.Contains(n.Gstin) &&
                    n.DeletedAt == null &&
                    !n.IsStaged)
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .Select(n => new CaDashboardNoticeSummary(
                    n.Id,
                    n.NoticeNumber,
                    n.NoticeType,
                    n.Gstin!,
                    group.Key.ClientName,
                    n.Status,
                    n.ResponseDeadline,
                    n.CreatedAt
                ))
                .ToListAsync(ct);

            result.AddRange(notices);
        }

        // Return top N across all clients, sorted by date
        return result
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToList();
    }

    private async Task<List<CaDashboardNoticeSummary>> GetRecentNoticesForClientAsync(
        Guid organizationId,
        List<string> authorizedGstins,
        int limit,
        CancellationToken ct)
    {
        return await _db.Notices
            .IgnoreQueryFilters()
            .Where(n =>
                n.OrganizationId == organizationId &&
                n.Gstin != null &&
                authorizedGstins.Contains(n.Gstin) &&
                n.DeletedAt == null &&
                !n.IsStaged)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new CaDashboardNoticeSummary(
                n.Id,
                n.NoticeNumber,
                n.NoticeType,
                n.Gstin!,
                "", // Client name not needed in client context
                n.Status,
                n.ResponseDeadline,
                n.CreatedAt
            ))
            .ToListAsync(ct);
    }

    private async Task<List<CaActivitySummary>> GetRecentActivityAsync(
        Guid organizationId,
        List<string> authorizedGstins,
        int limit,
        CancellationToken ct)
    {
        // Get recent activity logs for notices with authorized GSTINs
        var activities = await _db.ActivityLogs
            .Include(a => a.Notice)
            .Where(a =>
                a.Notice != null &&
                a.Notice.OrganizationId == organizationId &&
                a.Notice.Gstin != null &&
                authorizedGstins.Contains(a.Notice.Gstin))
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new CaActivitySummary(
                a.ActivityType,
                a.Message,
                "notice",
                a.NoticeId,
                a.CreatedAt
            ))
            .ToListAsync(ct);

        return activities;
    }

    private async Task<int> GetNoticeCountForGstinAsync(
        Guid organizationId,
        string gstin,
        CancellationToken ct)
    {
        return await _db.Notices
            .IgnoreQueryFilters()
            .CountAsync(n =>
                n.OrganizationId == organizationId &&
                n.Gstin == gstin &&
                n.DeletedAt == null &&
                !n.IsStaged,
                ct);
    }
}
