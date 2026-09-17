using EffortlessInsight.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Resolves whether a self-registered CA (ApplicationUser.IsCA) currently has an
/// active, admin-granted "Free CA Access" grant. Single source of truth for this
/// check, reused by the admin grant/revoke endpoints, FeatureAccessService, and
/// the CA authorization test suite.
/// </summary>
public interface ICaAccessService
{
    /// <summary>
    /// True if the given user has an active CaFreeAccessGrant. Always a live DB
    /// read (no caching of its own) so a revoke is visible on the very next call.
    /// </summary>
    Task<bool> HasActiveFreeAccessAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Organization IDs owned (Role == "owner") by the given user - the orgs whose
    /// subscription-feature access is affected by that user's Free CA Access grant.
    /// </summary>
    Task<List<Guid>> GetOwnedOrganizationIdsAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class CaAccessService : ICaAccessService
{
    private readonly ApplicationDbContext _dbContext;

    public CaAccessService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasActiveFreeAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CaFreeAccessGrants
            .AnyAsync(g => g.CaUserId == userId && g.IsActive && g.DeletedAt == null, cancellationToken);
    }

    public async Task<List<Guid>> GetOwnedOrganizationIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.OrganizationMembers
            .Where(m => m.UserId == userId && m.Role == "owner" && m.Status == "active" && m.Organization.DeletedAt == null)
            .Select(m => m.OrganizationId)
            .ToListAsync(cancellationToken);
    }
}
