using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for managing CA GSTIN authorizations.
/// </summary>
public class CaAuthorizationService : ICaAuthorizationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CaAuthorizationService> _logger;

    public CaAuthorizationService(
        ApplicationDbContext db,
        ILogger<CaAuthorizationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> HasAccessAsync(
        Guid caUserId,
        string gstin,
        string requiredPermission,
        CancellationToken ct = default)
    {
        return await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .AnyAsync(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Gstin == gstin &&
                a.Status == CaGstinAuthorizationStatus.Active &&
                a.CaClientRelationship.Status == CaClientRelationshipStatus.Active &&
                a.Permissions.Contains(requiredPermission),
                ct);
    }

    public async Task<List<CaGstinAuthorizationDto>> GetAuthorizationsAsync(
        Guid caUserId,
        CancellationToken ct = default)
    {
        var authorizations = await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .Include(a => a.OrganizationGstin)
            .Where(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Status == CaGstinAuthorizationStatus.Active &&
                a.CaClientRelationship.Status == CaClientRelationshipStatus.Active)
            .ToListAsync(ct);

        var result = new List<CaGstinAuthorizationDto>();
        foreach (var auth in authorizations)
        {
            var noticeCount = await GetNoticeCountForGstinAsync(
                auth.CaClientRelationship.OrganizationId,
                auth.Gstin,
                ct);

            result.Add(MapToDto(auth, noticeCount));
        }

        return result;
    }

    public async Task<List<CaGstinAuthorizationDto>> GetAuthorizationsByRelationshipAsync(
        Guid relationshipId,
        CancellationToken ct = default)
    {
        var authorizations = await _db.CaGstinAuthorizations
            .Include(a => a.OrganizationGstin)
            .Include(a => a.CaClientRelationship)
            .Where(a => a.CaClientRelationshipId == relationshipId)
            .ToListAsync(ct);

        var result = new List<CaGstinAuthorizationDto>();
        foreach (var auth in authorizations)
        {
            var noticeCount = await GetNoticeCountForGstinAsync(
                auth.CaClientRelationship.OrganizationId,
                auth.Gstin,
                ct);

            result.Add(MapToDto(auth, noticeCount));
        }

        return result;
    }

    public async Task<CaGstinAuthorization?> GetAuthorizationAsync(
        Guid caUserId,
        string gstin,
        CancellationToken ct = default)
    {
        return await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .Include(a => a.OrganizationGstin)
            .FirstOrDefaultAsync(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Gstin == gstin &&
                a.Status == CaGstinAuthorizationStatus.Active &&
                a.CaClientRelationship.Status == CaClientRelationshipStatus.Active,
                ct);
    }

    public async Task<bool> UpdatePermissionsAsync(
        Guid authorizationId,
        Guid boUserId,
        List<string> permissions,
        CancellationToken ct = default)
    {
        var authorization = await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .FirstOrDefaultAsync(a => a.Id == authorizationId, ct);

        if (authorization == null)
            return false;

        // Verify the BO is the client in the relationship
        if (authorization.CaClientRelationship.ClientUserId != boUserId)
            return false;

        // Validate permissions
        var validPermissions = permissions
            .Where(p => CaPermission.IsValid(p))
            .Distinct()
            .ToList();

        if (!validPermissions.Any())
            return false;

        authorization.Permissions = validPermissions;
        authorization.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("GSTIN permissions updated: {AuthId}, Permissions: {Perms}",
            authorizationId, string.Join(",", validPermissions));
        return true;
    }

    public async Task<bool> RevokeAsync(
        Guid authorizationId,
        Guid revokingUserId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var authorization = await _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .FirstOrDefaultAsync(a =>
                a.Id == authorizationId &&
                a.Status == CaGstinAuthorizationStatus.Active,
                ct);

        if (authorization == null)
            return false;

        // Verify the revoking user is either the CA or the client
        if (authorization.CaClientRelationship.CaUserId != revokingUserId &&
            authorization.CaClientRelationship.ClientUserId != revokingUserId)
            return false;

        authorization.Status = CaGstinAuthorizationStatus.Revoked;
        authorization.RevokedAt = DateTime.UtcNow;
        authorization.RevokedById = revokingUserId;
        authorization.RevocationReason = reason;
        authorization.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("GSTIN authorization revoked: {AuthId}, GSTIN: {Gstin}, By: {UserId}",
            authorizationId, authorization.Gstin, revokingUserId);
        return true;
    }

    public async Task LinkOrganizationGstinAsync(
        string gstin,
        Guid organizationGstinId,
        CancellationToken ct = default)
    {
        // Find all pending authorizations for this GSTIN
        var pendingAuthorizations = await _db.CaGstinAuthorizations
            .Where(a =>
                a.Gstin == gstin &&
                a.OrganizationGstinId == null &&
                a.Status == CaGstinAuthorizationStatus.PendingClaim)
            .ToListAsync(ct);

        foreach (var auth in pendingAuthorizations)
        {
            auth.OrganizationGstinId = organizationGstinId;
            auth.Status = CaGstinAuthorizationStatus.Active;
            auth.UpdatedAt = DateTime.UtcNow;
        }

        if (pendingAuthorizations.Any())
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Linked {Count} authorizations to OrganizationGstin: {OrgGstinId}",
                pendingAuthorizations.Count, organizationGstinId);
        }
    }

    public async Task<List<string>> GetAuthorizedGstinsAsync(
        Guid caUserId,
        Guid? organizationId = null,
        CancellationToken ct = default)
    {
        var query = _db.CaGstinAuthorizations
            .Include(a => a.CaClientRelationship)
            .Where(a =>
                a.CaClientRelationship.CaUserId == caUserId &&
                a.Status == CaGstinAuthorizationStatus.Active &&
                a.CaClientRelationship.Status == CaClientRelationshipStatus.Active);

        if (organizationId.HasValue)
        {
            query = query.Where(a => a.CaClientRelationship.OrganizationId == organizationId);
        }

        return await query
            .Select(a => a.Gstin)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<int> GetNoticeCountForGstinAsync(
        Guid? organizationId,
        string gstin,
        CancellationToken ct)
    {
        if (organizationId == null)
            return 0;

        return await _db.Notices
            .IgnoreQueryFilters()
            .CountAsync(n =>
                n.OrganizationId == organizationId &&
                n.Gstin == gstin &&
                n.DeletedAt == null,
                ct);
    }

    private static CaGstinAuthorizationDto MapToDto(CaGstinAuthorization auth, int noticeCount)
    {
        return new CaGstinAuthorizationDto(
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
        );
    }
}
