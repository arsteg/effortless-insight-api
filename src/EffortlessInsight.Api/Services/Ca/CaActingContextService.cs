using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Ca;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// The engagement a request is being made under, when a CA is acting for a client.
/// </summary>
/// <param name="CaUserId">The acting CA.</param>
/// <param name="RelationshipId">The CaClientRelationship authorising the access.</param>
/// <param name="ClientOrganizationId">The client organization the request is scoped to.</param>
/// <param name="CaBillingOrganizationId">The CA's own firm, which carries their entitlement.</param>
public sealed record CaActingContext(
    Guid CaUserId,
    Guid RelationshipId,
    Guid ClientOrganizationId,
    Guid CaBillingOrganizationId);

public interface ICaActingContextService
{
    /// <summary>
    /// Resolves and re-validates the acting engagement for the current request, or null
    /// when the caller is not a CA acting for a client. Cheap for ordinary users: it
    /// returns before touching the database when the ca_client_rel_id claim is absent.
    /// </summary>
    Task<CaActingContext?> ResolveAsync(CancellationToken ct = default);
}

/// <summary>
/// Re-validates the CA engagement behind a token on every request.
///
/// The token is signed and lives 15 minutes, so without this check a revoked or expired
/// engagement would keep working until the token rolled over. Resolution is memoised per
/// request, so the middleware and the billing checks share a single query.
/// </summary>
public class CaActingContextService : ICaActingContextService
{
    private const string HttpContextItemKey = "__CaActingContext";

    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CaActingContextService(
        ApplicationDbContext db,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<CaActingContext?> ResolveAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return null;
        }

        if (httpContext.Items.TryGetValue(HttpContextItemKey, out var cached))
        {
            return cached as CaActingContext;
        }

        var context = await ResolveCoreAsync(httpContext.User, ct);
        httpContext.Items[HttpContextItemKey] = context;
        return context;
    }

    private async Task<CaActingContext?> ResolveCoreAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        // Fast path for every non-CA request: no claim, no query.
        var relationshipClaim = user.FindFirst(CaClaimTypes.ClientRelationshipId)?.Value;
        if (string.IsNullOrEmpty(relationshipClaim) || !Guid.TryParse(relationshipClaim, out var relationshipId))
        {
            return null;
        }

        var userIdClaim = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var caUserId))
        {
            return null;
        }

        var orgClaim = user.FindFirst("org_id")?.Value;
        if (!Guid.TryParse(orgClaim, out var clientOrgId))
        {
            return null;
        }

        // The relationship must still be live, still belong to this CA, and still point at
        // the organization the token claims. That last check stops a token minted for one
        // client being replayed against another.
        var relationshipValid = await _db.CaClientRelationships
            .AnyAsync(r =>
                r.Id == relationshipId &&
                r.CaUserId == caUserId &&
                r.OrganizationId == clientOrgId &&
                r.Status == CaClientRelationshipStatus.Active &&
                (r.ExpiresAt == null || r.ExpiresAt > DateTime.UtcNow),
                ct);

        if (!relationshipValid)
        {
            return null;
        }

        var ca = await _db.Users
            .Where(u => u.Id == caUserId && u.IsCa && u.IsActive && u.DeletedAt == null)
            .Select(u => new { u.OrganizationId })
            .FirstOrDefaultAsync(ct);

        if (ca?.OrganizationId == null)
        {
            return null;
        }

        var profileActive = await _db.CaProfiles
            .AnyAsync(p => p.UserId == caUserId && p.Status == CaProfileStatus.Active, ct);

        if (!profileActive)
        {
            return null;
        }

        return new CaActingContext(
            CaUserId: caUserId,
            RelationshipId: relationshipId,
            ClientOrganizationId: clientOrgId,
            CaBillingOrganizationId: ca.OrganizationId.Value);
    }
}
