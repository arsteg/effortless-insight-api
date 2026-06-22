namespace EffortlessInsight.Api.Services;

/// <summary>
/// Provides the current tenant (organization) context for the request.
/// Used for defense-in-depth tenant isolation via global query filters.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current organization ID, or null if not in an organization context.
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// Sets the organization ID for the current request.
    /// </summary>
    void SetOrganizationId(Guid organizationId);

    /// <summary>
    /// Whether tenant filtering should be bypassed (e.g., for admin operations).
    /// </summary>
    bool BypassTenantFilter { get; }

    /// <summary>
    /// Temporarily disable tenant filtering for admin operations.
    /// </summary>
    void DisableTenantFilter();
}

/// <summary>
/// Scoped implementation of tenant context.
/// Each HTTP request gets its own instance.
/// </summary>
public class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }
    public bool BypassTenantFilter { get; private set; }

    public void SetOrganizationId(Guid organizationId)
    {
        OrganizationId = organizationId;
    }

    public void DisableTenantFilter()
    {
        BypassTenantFilter = true;
    }
}
