using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>Resolve only a CA's accepted client relationship, never a global GSTIN owner.</summary>
public static class CaNoticeRouting
{
    public static async Task<OrganizationGstin?> ResolveAsync(ApplicationDbContext db, Guid sourceOrg,
        Guid actor, string gstin, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (!await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == sourceOrg && m.UserId == actor
                && m.User.IsCA && m.Role == "owner" && m.Status == "active" && m.DeletedAt == null
                && (m.AccessExpiresAt == null || m.AccessExpiresAt > now), ct)) return null;
        var hash = CaWorkspaceWrites.Hash(gstin);
        var prospect = await db.CaProspectClients.AsNoTracking().SingleOrDefaultAsync(p => p.CaUserId == actor
            && p.GstinHash == hash && p.Status == "merged" && p.DeletedAt == null, ct);
        if (prospect?.MergedIntoOrganizationId is not Guid destination) return null;
        if (!await db.CaClientInvitations.AnyAsync(i => i.CaUserId == actor && i.CaOrganizationId == sourceOrg
                && i.ResultingOrganizationId == destination && i.Status == "accepted" && i.DeletedAt == null
                && i.Id == prospect.CaClientInvitationId, ct))
            throw new InvalidOperationException("CLIENT_ROUTING_CONFLICT: The accepted client relationship could not be verified.");
        var membership = await db.OrganizationMembers.AsNoTracking().Include(m => m.Organization).SingleOrDefaultAsync(m =>
            m.OrganizationId == destination && m.UserId == actor && m.Status == "active" && m.DeletedAt == null
            && (m.AccessExpiresAt == null || m.AccessExpiresAt > now), ct);
        if (membership == null || membership.Role is not ("ca" or "owner" or "admin" or "manager" or "member"))
            throw new UnauthorizedAccessException("CLIENT_ACCESS_REVOKED: Active write access to the BO organization is required.");
        var hasPlan = await db.BillingSubscriptions.AnyAsync(s => s.OrganizationId == destination && s.DeletedAt == null
            && ((s.Status == "active" && s.CurrentPeriodEnd > now) || (s.Status == "trialing" && s.TrialEnd > now)), ct);
        if (!hasPlan || membership.Organization.SubscriptionStatus is "paused" or "past_due" or "cancelled" or "expired")
            throw new InvalidOperationException("SUBSCRIPTION_REQUIRED: Activate the BO organization's subscription before routing notices.");
        var registry = await db.OrganizationGstins.Where(g => g.OrganizationId == destination && g.DeletedAt == null).ToListAsync(ct);
        return registry.SingleOrDefault(g => CaWorkspaceWrites.Hash(g.Gstin) == hash)
            ?? throw new InvalidOperationException("CLIENT_ROUTING_CONFLICT: GSTIN is missing from the accepted BO organization.");
    }
}
