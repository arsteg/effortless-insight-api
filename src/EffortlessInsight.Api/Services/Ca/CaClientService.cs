using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for managing CA-Client relationships.
/// </summary>
public class CaClientService : ICaClientService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CaClientService> _logger;

    public CaClientService(
        ApplicationDbContext db,
        ILogger<CaClientService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CaClientListResponse> GetClientsAsync(
        Guid caUserId,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.CaClientRelationships
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
            .Where(r => r.CaUserId == caUserId);

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(r => r.Status == status);
        }

        var total = await query.CountAsync(ct);
        var relationships = await query
            .OrderByDescending(r => r.AcceptedAt ?? r.InvitedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var summaries = new List<CaClientSummaryDto>();
        foreach (var r in relationships)
        {
            var noticeCounts = await GetNoticeCountsForRelationshipAsync(r, ct);
            var lastSync = await GetLastSyncAtAsync(r, ct);

            summaries.Add(new CaClientSummaryDto(
                RelationshipId: r.Id,
                ClientUserId: r.ClientUserId,
                ClientName: r.ClientUser.Name,
                ClientEmail: r.ClientUser.Email!,
                OrganizationName: r.Organization?.Name,
                Status: r.Status,
                AuthorizedGstinCount: r.GstinAuthorizations.Count(a => a.Status == CaGstinAuthorizationStatus.Active),
                TotalNoticeCount: noticeCounts.total,
                PendingNoticeCount: noticeCounts.pending,
                LastSyncAt: lastSync
            ));
        }

        return new CaClientListResponse(
            Items: summaries,
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: (int)Math.Ceiling((double)total / pageSize)
        );
    }

    public async Task<CaClientDto?> GetClientAsync(Guid caUserId, Guid relationshipId, CancellationToken ct = default)
    {
        var relationship = await _db.CaClientRelationships
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
                .ThenInclude(a => a.OrganizationGstin)
            .FirstOrDefaultAsync(r =>
                r.Id == relationshipId &&
                r.CaUserId == caUserId,
                ct);

        if (relationship == null)
            return null;

        var noticeCounts = await GetNoticeCountsForRelationshipAsync(relationship, ct);
        var lastSync = await GetLastSyncAtAsync(relationship, ct);

        var authorizations = relationship.GstinAuthorizations
            .Select(a => MapAuthorizationToDto(a, 0)) // Notice count per GSTIN would need a separate query
            .ToList();

        return new CaClientDto(
            RelationshipId: relationship.Id,
            ClientUserId: relationship.ClientUserId,
            ClientName: relationship.ClientUser.Name,
            ClientEmail: relationship.ClientUser.Email!,
            OrganizationId: relationship.OrganizationId,
            OrganizationName: relationship.Organization?.Name,
            Status: relationship.Status,
            InvitedAt: relationship.InvitedAt,
            AcceptedAt: relationship.AcceptedAt,
            ClientReference: relationship.ClientReference,
            GstinAuthorizations: authorizations,
            TotalNoticeCount: noticeCounts.total,
            PendingNoticeCount: noticeCounts.pending,
            LastSyncAt: lastSync
        );
    }

    public async Task<CaClientRelationship?> GetRelationshipAsync(
        Guid caUserId,
        Guid clientUserId,
        CancellationToken ct = default)
    {
        return await _db.CaClientRelationships
            .Include(r => r.GstinAuthorizations)
            .FirstOrDefaultAsync(r =>
                r.CaUserId == caUserId &&
                r.ClientUserId == clientUserId,
                ct);
    }

    public async Task<bool> UpdateClientAsync(
        Guid caUserId,
        Guid relationshipId,
        UpdateCaClientRequest request,
        CancellationToken ct = default)
    {
        var relationship = await _db.CaClientRelationships
            .FirstOrDefaultAsync(r =>
                r.Id == relationshipId &&
                r.CaUserId == caUserId,
                ct);

        if (relationship == null)
            return false;

        if (request.ClientReference != null)
            relationship.ClientReference = request.ClientReference;

        if (request.Notes != null)
            relationship.Notes = request.Notes;

        relationship.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Client relationship updated: {RelationshipId}", relationshipId);
        return true;
    }

    public async Task<bool> RevokeAsync(
        Guid relationshipId,
        Guid revokingUserId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var relationship = await _db.CaClientRelationships
            .Include(r => r.GstinAuthorizations)
            .FirstOrDefaultAsync(r => r.Id == relationshipId, ct);

        if (relationship == null)
            return false;

        // Verify the revoking user is either the CA or the client
        if (relationship.CaUserId != revokingUserId && relationship.ClientUserId != revokingUserId)
            return false;

        // Revoke the relationship
        relationship.Status = CaClientRelationshipStatus.Revoked;
        relationship.RevokedAt = DateTime.UtcNow;
        relationship.RevokedById = revokingUserId;
        relationship.RevocationReason = reason;
        relationship.UpdatedAt = DateTime.UtcNow;

        // Revoke all GSTIN authorizations
        foreach (var auth in relationship.GstinAuthorizations.Where(a => a.Status == CaGstinAuthorizationStatus.Active))
        {
            auth.Status = CaGstinAuthorizationStatus.Revoked;
            auth.RevokedAt = DateTime.UtcNow;
            auth.RevokedById = revokingUserId;
            auth.RevocationReason = reason;
            auth.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Relationship revoked: {RelationshipId}, By: {UserId}", relationshipId, revokingUserId);
        return true;
    }

    public async Task<bool> LinkOrganizationAsync(
        Guid clientUserId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        // Find all pending relationships for this client
        var relationships = await _db.CaClientRelationships
            .Include(r => r.GstinAuthorizations)
            .Where(r =>
                r.ClientUserId == clientUserId &&
                r.OrganizationId == null &&
                r.Status == CaClientRelationshipStatus.PendingInvitation)
            .ToListAsync(ct);

        if (!relationships.Any())
            return false;

        // Get organization's GSTINs
        var orgGstins = await _db.OrganizationGstins
            .Where(g => g.OrganizationId == organizationId)
            .ToListAsync(ct);

        var orgGstinDict = orgGstins.ToDictionary(g => g.Gstin, g => g.Id);

        foreach (var relationship in relationships)
        {
            relationship.OrganizationId = organizationId;
            relationship.Status = CaClientRelationshipStatus.Active;
            relationship.UpdatedAt = DateTime.UtcNow;

            // Link GSTIN authorizations to OrganizationGstin records
            foreach (var auth in relationship.GstinAuthorizations)
            {
                if (orgGstinDict.TryGetValue(auth.Gstin, out var orgGstinId))
                {
                    auth.OrganizationGstinId = orgGstinId;
                    auth.Status = CaGstinAuthorizationStatus.Active;
                    auth.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Organization linked to {Count} relationships: {OrgId}, Client: {ClientId}",
            relationships.Count, organizationId, clientUserId);
        return true;
    }

    public async Task<List<CaClientDto>> GetCasForOrganizationAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        var relationships = await _db.CaClientRelationships
            .Include(r => r.CaUser)
                .ThenInclude(u => u.CaProfile)
            .Include(r => r.GstinAuthorizations)
            .Where(r =>
                r.OrganizationId == organizationId &&
                r.Status == CaClientRelationshipStatus.Active)
            .ToListAsync(ct);

        return relationships.Select(r => new CaClientDto(
            RelationshipId: r.Id,
            ClientUserId: r.CaUserId, // In this context, the "client" is the CA
            ClientName: r.CaUser.Name,
            ClientEmail: r.CaUser.Email!,
            OrganizationId: r.OrganizationId,
            OrganizationName: null,
            Status: r.Status,
            InvitedAt: r.InvitedAt,
            AcceptedAt: r.AcceptedAt,
            ClientReference: r.ClientReference,
            GstinAuthorizations: r.GstinAuthorizations
                .Where(a => a.Status == CaGstinAuthorizationStatus.Active)
                .Select(a => MapAuthorizationToDto(a, 0))
                .ToList(),
            TotalNoticeCount: 0,
            PendingNoticeCount: 0,
            LastSyncAt: null
        )).ToList();
    }

    public async Task ExpireRelationshipsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiredRelationships = await _db.CaClientRelationships
            .Include(r => r.GstinAuthorizations)
            .Where(r =>
                r.Status == CaClientRelationshipStatus.Active &&
                r.ExpiresAt != null &&
                r.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var relationship in expiredRelationships)
        {
            relationship.Status = CaClientRelationshipStatus.Expired;
            relationship.UpdatedAt = now;

            foreach (var auth in relationship.GstinAuthorizations.Where(a => a.Status == CaGstinAuthorizationStatus.Active))
            {
                auth.Status = CaGstinAuthorizationStatus.Revoked;
                auth.RevokedAt = now;
                auth.RevocationReason = "Relationship expired";
                auth.UpdatedAt = now;
            }
        }

        if (expiredRelationships.Any())
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} relationships", expiredRelationships.Count);
        }
    }

    private async Task<(int total, int pending)> GetNoticeCountsForRelationshipAsync(
        CaClientRelationship relationship,
        CancellationToken ct)
    {
        if (relationship.OrganizationId == null)
            return (0, 0);

        var activeGstins = relationship.GstinAuthorizations
            .Where(a => a.Status == CaGstinAuthorizationStatus.Active)
            .Select(a => a.Gstin)
            .ToList();

        if (!activeGstins.Any())
            return (0, 0);

        var total = await _db.Notices
            .IgnoreQueryFilters()
            .CountAsync(n =>
                n.OrganizationId == relationship.OrganizationId &&
                n.Gstin != null &&
                activeGstins.Contains(n.Gstin) &&
                n.DeletedAt == null,
                ct);

        var pending = await _db.Notices
            .IgnoreQueryFilters()
            .CountAsync(n =>
                n.OrganizationId == relationship.OrganizationId &&
                n.Gstin != null &&
                activeGstins.Contains(n.Gstin) &&
                n.DeletedAt == null &&
                (n.Status == "uploaded" || n.Status == "processing" || n.Status == "analyzed" || n.Status == "in_progress"),
                ct);

        return (total, pending);
    }

    private async Task<DateTime?> GetLastSyncAtAsync(CaClientRelationship relationship, CancellationToken ct)
    {
        if (relationship.OrganizationId == null)
            return null;

        var activeGstins = relationship.GstinAuthorizations
            .Where(a => a.Status == CaGstinAuthorizationStatus.Active)
            .Select(a => a.Gstin)
            .ToList();

        if (!activeGstins.Any())
            return null;

        return await _db.GstClients
            .Where(c =>
                c.OrganizationId == relationship.OrganizationId &&
                activeGstins.Contains(c.Gstin))
            .MaxAsync(c => (DateTime?)c.LastSuccessfulSyncAt, ct);
    }

    private static CaGstinAuthorizationDto MapAuthorizationToDto(CaGstinAuthorization auth, int noticeCount)
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
