using EffortlessInsight.Api.Services.Ca;

namespace EffortlessInsight.Api.Services.Billing;

public interface IEffectiveBillingOrganizationService
{
    /// <summary>
    /// The organization whose plan should decide feature access for this request.
    /// Normally the requested organization; for a CA acting on behalf of a client with a
    /// live entitlement, their own firm.
    /// </summary>
    Task<Guid> ResolveAsync(Guid requestedOrganizationId, CancellationToken ct = default);
}

/// <summary>
/// Picks which organization's plan governs feature access.
///
/// Deliberately a wrapper rather than a change inside <see cref="FeatureAccessService"/>:
/// that service caches under org_features:{organizationId}, so resolving the CA fallback
/// in there would write a CA-influenced feature list under the *client's* cache key and
/// then serve it to the client's own Business Owner. Resolving first keeps every cache
/// entry attributable to the plan it actually came from.
/// </summary>
public class EffectiveBillingOrganizationService : IEffectiveBillingOrganizationService
{
    private readonly ICaActingContextService _caActingContext;
    private readonly ISubscriptionStatusEvaluator _evaluator;

    public EffectiveBillingOrganizationService(
        ICaActingContextService caActingContext,
        ISubscriptionStatusEvaluator evaluator)
    {
        _caActingContext = caActingContext;
        _evaluator = evaluator;
    }

    public async Task<Guid> ResolveAsync(Guid requestedOrganizationId, CancellationToken ct = default)
    {
        var acting = await _caActingContext.ResolveAsync(ct);

        // Returns null for everyone who is not a CA acting for a client, without touching
        // the database, so ordinary requests are unaffected.
        if (acting == null || acting.ClientOrganizationId != requestedOrganizationId)
        {
            return requestedOrganizationId;
        }

        return await _evaluator.HasLiveCaEntitlementAsync(acting.CaBillingOrganizationId, ct)
            ? acting.CaBillingOrganizationId
            : requestedOrganizationId;
    }
}
