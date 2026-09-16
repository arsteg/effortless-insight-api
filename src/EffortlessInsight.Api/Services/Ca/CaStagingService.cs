using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for CA staging (pre-claim notice storage).
/// </summary>
public class CaStagingService : ICaStagingService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CaStagingService> _logger;

    public CaStagingService(
        ApplicationDbContext db,
        ILogger<CaStagingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> StageNoticesAsync(
        Guid caUserId,
        string gstin,
        Guid targetClientUserId,
        List<Notice> notices,
        CancellationToken ct = default)
    {
        if (!notices.Any())
            return 0;

        // Mark notices as staged for the target client
        foreach (var notice in notices)
        {
            notice.IsStaged = true;
            notice.StagedForClientUserId = targetClientUserId;
            notice.CaSyncedByUserId = caUserId;
            notice.Source = NoticeSource.CaStagedForClient;
        }

        // Update invitation staged count
        var invitation = await _db.CaInvitations
            .FirstOrDefaultAsync(i =>
                i.InviterUserId == caUserId &&
                i.Gstin == gstin &&
                i.AcceptedUserId == targetClientUserId &&
                i.Status == Data.Entities.Ca.CaInvitationStatus.Pending,
                ct);

        if (invitation != null)
        {
            invitation.StagedNoticeCount += notices.Count;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Staged {Count} notices: CA={CaUserId}, GSTIN={Gstin}, Client={ClientId}",
            notices.Count, caUserId, gstin, targetClientUserId);

        return notices.Count;
    }

    public async Task<int> TransferStagedNoticesAsync(
        Guid clientUserId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        // Find all staged notices for this client
        var stagedNotices = await _db.Notices
            .IgnoreQueryFilters()
            .Where(n =>
                n.IsStaged &&
                n.StagedForClientUserId == clientUserId &&
                n.DeletedAt == null)
            .ToListAsync(ct);

        if (!stagedNotices.Any())
            return 0;

        // Get organization GSTINs for linking
        var orgGstins = await _db.OrganizationGstins
            .Where(g => g.OrganizationId == organizationId)
            .ToDictionaryAsync(g => g.Gstin, g => g.Id, ct);

        foreach (var notice in stagedNotices)
        {
            // Transfer to client's organization
            notice.OrganizationId = organizationId;
            notice.IsStaged = false;
            notice.StagedForClientUserId = null;
            notice.Source = NoticeSource.CaSynced;
            notice.UpdatedAt = DateTime.UtcNow;

            // Link to OrganizationGstin if available
            if (notice.Gstin != null && orgGstins.TryGetValue(notice.Gstin, out var gstinId))
            {
                notice.GstinId = gstinId;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Transferred {Count} staged notices: Client={ClientId}, Org={OrgId}",
            stagedNotices.Count, clientUserId, organizationId);

        return stagedNotices.Count;
    }

    public async Task<int> GetStagedNoticeCountAsync(
        Guid caUserId,
        string gstin,
        Guid? targetClientUserId = null,
        CancellationToken ct = default)
    {
        var query = _db.Notices
            .IgnoreQueryFilters()
            .Where(n =>
                n.IsStaged &&
                n.CaSyncedByUserId == caUserId &&
                n.Gstin == gstin &&
                n.DeletedAt == null);

        if (targetClientUserId.HasValue)
        {
            query = query.Where(n => n.StagedForClientUserId == targetClientUserId);
        }

        return await query.CountAsync(ct);
    }
}
