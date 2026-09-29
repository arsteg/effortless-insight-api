using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Represents the visibility context for cross-organization notice queries.
/// </summary>
public record CrossOrgVisibilityContext(
    /// <summary>The user's primary/current organization ID.</summary>
    Guid PrimaryOrganizationId,
    /// <summary>Organizations linked through CA-BO relationships.</summary>
    List<LinkedOrganization> LinkedOrganizations,
    /// <summary>All GSTIN hashes the user has cross-org visibility for.</summary>
    HashSet<string> VisibleGstinHashes)
{
    /// <summary>
    /// Returns true if there are any linked organizations (cross-org visibility enabled).
    /// </summary>
    public bool HasLinkedOrganizations => LinkedOrganizations.Count > 0;
}

/// <summary>
/// Represents a linked organization for cross-org notice visibility.
/// </summary>
public record LinkedOrganization(
    /// <summary>The linked organization's ID.</summary>
    Guid OrganizationId,
    /// <summary>The linked organization's name.</summary>
    string OrganizationName,
    /// <summary>The user's role in the relationship: "ca" if user is CA viewing BO's notices, "bo" if user is BO viewing CA's notices.</summary>
    string Role,
    /// <summary>GSTIN hashes that grant visibility to this organization's notices.</summary>
    HashSet<string> LinkedGstinHashes,
    /// <summary>True if the current user is the CA in this relationship.</summary>
    bool IsCurrentUserCa);

/// <summary>
/// Service for managing cross-organization notice visibility.
/// Enables CAs and BOs to see each other's notices for shared GSTINs.
/// </summary>
public interface ICrossOrgNoticeVisibilityService
{
    /// <summary>
    /// Gets the visibility context for a user in the current organization.
    /// This determines which linked organizations' notices the user can see.
    /// </summary>
    Task<CrossOrgVisibilityContext> GetVisibilityContextAsync(
        Guid userId,
        Guid currentOrganizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user can access a specific notice (either in their org or a linked org).
    /// </summary>
    Task<bool> CanAccessNoticeAsync(
        Guid userId,
        Guid noticeId,
        Guid currentOrganizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the SHA-256 hash of a normalized GSTIN.
    /// Used for cross-org visibility queries.
    /// </summary>
    static string ComputeGstinHash(string gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin))
            return string.Empty;

        var normalized = gstin.Trim().ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }
}

/// <summary>
/// Implementation of cross-organization notice visibility service.
/// </summary>
public class CrossOrgNoticeVisibilityService : ICrossOrgNoticeVisibilityService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CrossOrgNoticeVisibilityService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    public CrossOrgNoticeVisibilityService(
        ApplicationDbContext db,
        IMemoryCache cache,
        ILogger<CrossOrgNoticeVisibilityService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CrossOrgVisibilityContext> GetVisibilityContextAsync(
        Guid userId,
        Guid currentOrganizationId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"cross_org_visibility:{userId}:{currentOrganizationId}";

        if (_cache.TryGetValue<CrossOrgVisibilityContext>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var context = await BuildVisibilityContextAsync(userId, currentOrganizationId, cancellationToken);

        _cache.Set(cacheKey, context, CacheDuration);

        return context;
    }

    private async Task<CrossOrgVisibilityContext> BuildVisibilityContextAsync(
        Guid userId,
        Guid currentOrganizationId,
        CancellationToken cancellationToken)
    {
        var linkedOrganizations = new List<LinkedOrganization>();
        var allVisibleGstinHashes = new HashSet<string>();

        // Check if user has a membership in the current organization
        var currentMembership = await _db.OrganizationMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.UserId == userId &&
                m.OrganizationId == currentOrganizationId &&
                m.Status == "active" &&
                m.DeletedAt == null &&
                (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow),
                cancellationToken);

        if (currentMembership == null)
        {
            _logger.LogDebug(
                "User {UserId} has no active membership in organization {OrgId}",
                userId, currentOrganizationId);
            return new CrossOrgVisibilityContext(currentOrganizationId, [], []);
        }

        // Case 1: User is a CA (external member with role="ca") in the current org
        // This means they're viewing a BO's organization, so they can see the CA org's notices
        if (currentMembership.Role == "ca" && currentMembership.IsExternal)
        {
            // Find the CA's own organization (where they are owner)
            var caOwnOrg = await _db.OrganizationMembers
                .AsNoTracking()
                .Where(m =>
                    m.UserId == userId &&
                    m.Role == "owner" &&
                    m.Status == "active" &&
                    m.DeletedAt == null &&
                    m.Organization.DeletedAt == null)
                .Select(m => new { m.OrganizationId, m.Organization.Name })
                .FirstOrDefaultAsync(cancellationToken);

            if (caOwnOrg != null)
            {
                // Find the CaBoGstinLinks that connect CA's org to this BO's org
                var links = await _db.CaBoGstinLinks
                    .AsNoTracking()
                    .Where(l =>
                        l.CaOrganizationId == caOwnOrg.OrganizationId &&
                        l.BoOrganizationId == currentOrganizationId &&
                        l.IsActive &&
                        l.DeletedAt == null)
                    .Select(l => l.GstinHash)
                    .ToListAsync(cancellationToken);

                if (links.Count > 0)
                {
                    var gstinHashes = links.ToHashSet();
                    allVisibleGstinHashes.UnionWith(gstinHashes);

                    linkedOrganizations.Add(new LinkedOrganization(
                        OrganizationId: caOwnOrg.OrganizationId,
                        OrganizationName: caOwnOrg.Name,
                        Role: "ca",
                        LinkedGstinHashes: gstinHashes,
                        IsCurrentUserCa: true));
                }
            }
        }
        // Case 2: User is a regular member (owner, admin, member, etc.) in their own org
        // They can see notices from CAs who are connected to their org
        else if (currentMembership.Role != "ca")
        {
            // Find all CAs connected to this organization via CaBoGstinLinks
            var caLinks = await _db.CaBoGstinLinks
                .AsNoTracking()
                .Where(l =>
                    l.BoOrganizationId == currentOrganizationId &&
                    l.IsActive &&
                    l.DeletedAt == null)
                .GroupBy(l => new { l.CaOrganizationId, l.CaOrganization.Name })
                .Select(g => new
                {
                    g.Key.CaOrganizationId,
                    CaOrganizationName = g.Key.Name,
                    GstinHashes = g.Select(l => l.GstinHash).ToList()
                })
                .ToListAsync(cancellationToken);

            foreach (var caLink in caLinks)
            {
                var gstinHashes = caLink.GstinHashes.ToHashSet();
                allVisibleGstinHashes.UnionWith(gstinHashes);

                linkedOrganizations.Add(new LinkedOrganization(
                    OrganizationId: caLink.CaOrganizationId,
                    OrganizationName: caLink.CaOrganizationName,
                    Role: "bo",
                    LinkedGstinHashes: gstinHashes,
                    IsCurrentUserCa: false));
            }
        }

        _logger.LogDebug(
            "Built visibility context for user {UserId} in org {OrgId}: {LinkedOrgCount} linked orgs, {GstinCount} GSTINs",
            userId, currentOrganizationId, linkedOrganizations.Count, allVisibleGstinHashes.Count);

        return new CrossOrgVisibilityContext(
            currentOrganizationId,
            linkedOrganizations,
            allVisibleGstinHashes);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessNoticeAsync(
        Guid userId,
        Guid noticeId,
        Guid currentOrganizationId,
        CancellationToken cancellationToken = default)
    {
        // First, check if the notice belongs to the current organization (direct access)
        var notice = await _db.Notices
            .AsNoTracking()
            .Where(n => n.Id == noticeId && n.DeletedAt == null)
            .Select(n => new { n.OrganizationId, n.GstinHash })
            .FirstOrDefaultAsync(cancellationToken);

        if (notice == null)
        {
            return false;
        }

        // Direct access: notice is in the current organization
        if (notice.OrganizationId == currentOrganizationId)
        {
            return true;
        }

        // Cross-org access: check if user has visibility to this notice
        if (string.IsNullOrEmpty(notice.GstinHash))
        {
            return false; // No GSTIN hash = no cross-org visibility possible
        }

        var visibilityContext = await GetVisibilityContextAsync(userId, currentOrganizationId, cancellationToken);

        // Check if the notice's organization is a linked org AND the GSTIN hash is in the visible set
        var linkedOrg = visibilityContext.LinkedOrganizations
            .FirstOrDefault(lo => lo.OrganizationId == notice.OrganizationId);

        if (linkedOrg == null)
        {
            return false;
        }

        // Verify the notice's GSTIN hash is in the set of GSTINs that grant visibility to this org
        return linkedOrg.LinkedGstinHashes.Contains(notice.GstinHash);
    }
}
