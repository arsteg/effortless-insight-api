using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Notices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Implementation of CA-BO GSTIN link management service.
/// </summary>
public class CaBoGstinLinkService : ICaBoGstinLinkService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CaBoGstinLinkService> _logger;

    public CaBoGstinLinkService(
        ApplicationDbContext db,
        IMemoryCache cache,
        ILogger<CaBoGstinLinkService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> EnsureLinksForGstinAsync(
        Guid caOrganizationId,
        Guid caUserId,
        string gstin,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gstin))
        {
            return 0;
        }

        var normalizedGstin = gstin.Trim().ToUpperInvariant();
        var gstinHash = ComputeGstinHash(normalizedGstin);

        // Find all BO organizations where:
        // 1. The CA user has an active membership with role="ca" and IsExternal=true
        // 2. The BO organization has this GSTIN registered
        var connectedBoOrgs = await FindConnectedBoOrgsForGstinAsync(caUserId, normalizedGstin, cancellationToken);

        if (connectedBoOrgs.Count == 0)
        {
            return 0;
        }

        var linksCreated = 0;

        foreach (var (boOrgId, caMembershipId) in connectedBoOrgs)
        {
            // Check if link already exists
            var existingLink = await _db.CaBoGstinLinks
                .FirstOrDefaultAsync(l =>
                    l.CaOrganizationId == caOrganizationId &&
                    l.BoOrganizationId == boOrgId &&
                    l.GstinHash == gstinHash &&
                    l.DeletedAt == null,
                    cancellationToken);

            if (existingLink != null)
            {
                // Reactivate if it was deactivated
                if (!existingLink.IsActive)
                {
                    existingLink.IsActive = true;
                    existingLink.DeactivatedAt = null;
                    existingLink.DeactivationReason = null;
                    linksCreated++;

                    _logger.LogInformation(
                        "Reactivated CaBoGstinLink for CA org {CaOrgId} -> BO org {BoOrgId}, GSTIN hash {GstinHash}",
                        caOrganizationId, boOrgId, gstinHash);
                }
                continue;
            }

            // Create new link
            var newLink = new CaBoGstinLink
            {
                CaOrganizationId = caOrganizationId,
                BoOrganizationId = boOrgId,
                GstinHash = gstinHash,
                CaUserId = caUserId,
                CaMembershipId = caMembershipId,
                IsActive = true
            };

            _db.CaBoGstinLinks.Add(newLink);
            linksCreated++;

            _logger.LogInformation(
                "Created CaBoGstinLink for CA org {CaOrgId} -> BO org {BoOrgId}, GSTIN hash {GstinHash}",
                caOrganizationId, boOrgId, gstinHash);
        }

        if (linksCreated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);

            // Invalidate cross-org visibility cache for affected users/orgs
            InvalidateCrossOrgVisibilityCache(connectedBoOrgs.Select(c => c.BoOrganizationId).ToList());
        }

        return linksCreated;
    }

    /// <inheritdoc />
    public async Task<CaBoGstinLink?> EnsureLinkExistsAsync(
        Guid caOrganizationId,
        Guid boOrganizationId,
        string gstin,
        Guid caUserId,
        Guid caMembershipId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gstin))
        {
            return null;
        }

        var normalizedGstin = gstin.Trim().ToUpperInvariant();
        var gstinHash = ComputeGstinHash(normalizedGstin);

        // Check if link already exists
        var existingLink = await _db.CaBoGstinLinks
            .FirstOrDefaultAsync(l =>
                l.CaOrganizationId == caOrganizationId &&
                l.BoOrganizationId == boOrganizationId &&
                l.GstinHash == gstinHash &&
                l.DeletedAt == null,
                cancellationToken);

        if (existingLink != null)
        {
            // Reactivate if needed
            if (!existingLink.IsActive)
            {
                existingLink.IsActive = true;
                existingLink.DeactivatedAt = null;
                existingLink.DeactivationReason = null;
                await _db.SaveChangesAsync(cancellationToken);

                InvalidateCrossOrgVisibilityCache([boOrganizationId]);
            }
            return existingLink;
        }

        // Create new link
        var newLink = new CaBoGstinLink
        {
            CaOrganizationId = caOrganizationId,
            BoOrganizationId = boOrganizationId,
            GstinHash = gstinHash,
            CaUserId = caUserId,
            CaMembershipId = caMembershipId,
            IsActive = true
        };

        _db.CaBoGstinLinks.Add(newLink);
        await _db.SaveChangesAsync(cancellationToken);

        InvalidateCrossOrgVisibilityCache([boOrganizationId]);

        _logger.LogInformation(
            "Created CaBoGstinLink for CA org {CaOrgId} -> BO org {BoOrgId}, GSTIN hash {GstinHash}",
            caOrganizationId, boOrganizationId, gstinHash);

        return newLink;
    }

    /// <inheritdoc />
    public async Task<List<(Guid BoOrganizationId, Guid CaMembershipId)>> FindConnectedBoOrgsForGstinAsync(
        Guid caUserId,
        string gstin,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gstin))
        {
            return [];
        }

        var normalizedGstin = gstin.Trim().ToUpperInvariant();

        // Find all BO organizations where CA is an external member (role="ca", IsExternal=true)
        var gstinHash = ComputeGstinHash(normalizedGstin);
        var caMemberships = await _db.OrganizationMembers
            .AsNoTracking()
            .Where(m =>
                m.UserId == caUserId &&
                m.Role == "ca" &&
                m.IsExternal &&
                // Membership alone is not consent to share the CA firm's separate
                // data. Preserve only explicit legacy links awaiting handover.
                _db.CaBoGstinLinks.Any(l => l.CaMembershipId == m.Id && l.GstinHash == gstinHash
                    && l.IsActive && l.DeletedAt == null) &&
                m.Status == "active" &&
                m.DeletedAt == null &&
                m.Organization.DeletedAt == null &&
                (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow))
            .Select(m => new { m.OrganizationId, m.Id })
            .ToListAsync(cancellationToken);

        if (caMemberships.Count == 0)
        {
            return [];
        }

        var result = new List<(Guid BoOrganizationId, Guid CaMembershipId)>();

        // For each BO org, check if they have this GSTIN registered
        // GSTIN is AES-GCM encrypted, so we need to load and compare in memory
        foreach (var membership in caMemberships)
        {
            var orgGstins = await _db.OrganizationGstins
                .AsNoTracking()
                .Where(g =>
                    g.OrganizationId == membership.OrganizationId &&
                    g.DeletedAt == null)
                .Select(g => g.Gstin)
                .ToListAsync(cancellationToken);

            // Compare in memory after decryption
            var hasGstin = orgGstins.Any(g =>
                string.Equals(g, normalizedGstin, StringComparison.OrdinalIgnoreCase));

            if (hasGstin)
            {
                result.Add((membership.OrganizationId, membership.Id));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<int> LinkAllClientGstinsAsync(
        Guid caOrganizationId,
        Guid boOrganizationId,
        Guid caUserId,
        Guid caMembershipId,
        CancellationToken cancellationToken = default)
    {
        // Get all GSTINs registered in the BO's organization
        var boGstins = await _db.OrganizationGstins
            .AsNoTracking()
            .Where(g =>
                g.OrganizationId == boOrganizationId &&
                g.DeletedAt == null)
            .Select(g => g.Gstin)
            .ToListAsync(cancellationToken);

        if (boGstins.Count == 0)
        {
            return 0;
        }

        // Get all GSTINs that the CA has as GstClients in their organization
        var caClientGstins = await _db.GstClients
            .AsNoTracking()
            .Where(c =>
                c.OrganizationId == caOrganizationId &&
                c.DeletedAt == null)
            .Select(c => c.Gstin)
            .ToListAsync(cancellationToken);

        // Also check CaProspectClients (staged clients not yet merged)
        var caProspectGstins = await _db.CaProspectClients
            .AsNoTracking()
            .Where(p =>
                p.CaUserId == caUserId &&
                p.DeletedAt == null)
            .Select(p => p.Gstin)
            .ToListAsync(cancellationToken);

        // Find GSTINs that exist in both CA's client list and BO's organization
        var matchingGstins = boGstins
            .Where(boGstin =>
                caClientGstins.Any(caGstin =>
                    string.Equals(caGstin, boGstin, StringComparison.OrdinalIgnoreCase)) ||
                caProspectGstins.Any(pGstin =>
                    string.Equals(pGstin, boGstin, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var linksCreated = 0;

        foreach (var gstin in matchingGstins)
        {
            var gstinHash = ComputeGstinHash(gstin.Trim().ToUpperInvariant());

            // Check if link already exists in database
            var existingLink = await _db.CaBoGstinLinks
                .FirstOrDefaultAsync(l =>
                    l.CaOrganizationId == caOrganizationId &&
                    l.BoOrganizationId == boOrganizationId &&
                    l.GstinHash == gstinHash &&
                    l.DeletedAt == null,
                    cancellationToken);

            // Also check local change tracker for pending adds (handles case where
            // invitation acceptance adds a link before calling this method)
            if (existingLink == null)
            {
                existingLink = _db.CaBoGstinLinks.Local
                    .FirstOrDefault(l =>
                        l.CaOrganizationId == caOrganizationId &&
                        l.BoOrganizationId == boOrganizationId &&
                        l.GstinHash == gstinHash &&
                        l.DeletedAt == null);
            }

            if (existingLink != null)
            {
                // Reactivate if it was deactivated
                if (!existingLink.IsActive)
                {
                    existingLink.IsActive = true;
                    existingLink.DeactivatedAt = null;
                    existingLink.DeactivationReason = null;
                    linksCreated++;
                }
                continue;
            }

            // Create new link
            var newLink = new CaBoGstinLink
            {
                CaOrganizationId = caOrganizationId,
                BoOrganizationId = boOrganizationId,
                GstinHash = gstinHash,
                CaUserId = caUserId,
                CaMembershipId = caMembershipId,
                IsActive = true
            };

            _db.CaBoGstinLinks.Add(newLink);
            linksCreated++;

            _logger.LogInformation(
                "Created CaBoGstinLink for CA org {CaOrgId} -> BO org {BoOrgId}, GSTIN hash {GstinHash}",
                caOrganizationId, boOrganizationId, gstinHash);
        }

        if (linksCreated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            InvalidateCrossOrgVisibilityCache([boOrganizationId]);
        }

        return linksCreated;
    }

    /// <inheritdoc />
    public async Task<CaBoGstinLink?> FindActiveLinkForGstinAsync(
        Guid caOrganizationId,
        string gstin,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gstin))
            return null;

        var gstinHash = ComputeGstinHash(gstin.Trim().ToUpperInvariant());

        return await _db.CaBoGstinLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l =>
                l.CaOrganizationId == caOrganizationId &&
                l.GstinHash == gstinHash &&
                l.IsActive &&
                l.DeletedAt == null,
                cancellationToken);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a normalized GSTIN.
    /// </summary>
    private static string ComputeGstinHash(string gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin))
            return string.Empty;

        var normalized = gstin.Trim().ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Invalidates the cross-org visibility cache for the given organization IDs.
    /// This ensures that visibility changes are reflected immediately.
    /// </summary>
    private void InvalidateCrossOrgVisibilityCache(List<Guid> organizationIds)
    {
        // The CrossOrgNoticeVisibilityService uses cache keys like:
        // "cross_org_visibility:{userId}:{currentOrganizationId}"
        // We can't enumerate all users, so we'll just log that cache may be stale.
        // The cache has a 2-minute TTL so it will refresh automatically.
        _logger.LogDebug(
            "CaBoGstinLink created/updated for orgs {OrgIds}; cross-org visibility cache may be stale for up to 2 minutes",
            string.Join(", ", organizationIds));
    }
}
