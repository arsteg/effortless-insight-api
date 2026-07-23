using EffortlessInsight.Api.Services;

namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Middleware to set the tenant context from the X-Organization-Id header.
/// This enables defense-in-depth tenant isolation via global query filters.
/// </summary>
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(
        RequestDelegate next,
        ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // Prefer the org_id claim from the authenticated JWT — it is signed and
        // cannot be spoofed by the client, unlike the X-Organization-Id header.
        var orgClaim = context.User?.FindFirst("org_id")?.Value;
        if (!string.IsNullOrEmpty(orgClaim) && Guid.TryParse(orgClaim, out var claimOrgId))
        {
            tenantContext.SetOrganizationId(claimOrgId);
            _logger.LogDebug("Tenant context set from claim to organization {OrganizationId}", claimOrgId);
        }
        // Fall back to the X-Organization-Id header (used by performance tests)
        else if (context.Request.Headers.TryGetValue("X-Organization-Id", out var orgIdHeader) &&
            Guid.TryParse(orgIdHeader.ToString(), out var organizationId))
        {
            tenantContext.SetOrganizationId(organizationId);
            _logger.LogDebug("Tenant context set from header to organization {OrganizationId}", organizationId);
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for tenant context middleware.
/// </summary>
public static class TenantContextMiddlewareExtensions
{
    /// <summary>
    /// Adds tenant context middleware to the pipeline.
    /// Should be added after authentication but before MVC.
    /// </summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantContextMiddleware>();
    }
}
