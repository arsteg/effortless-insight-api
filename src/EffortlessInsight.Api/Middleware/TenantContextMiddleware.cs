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
    private readonly IHostEnvironment _environment;

    public TenantContextMiddleware(
        RequestDelegate next,
        ILogger<TenantContextMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
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
        // Fall back to the unsigned X-Organization-Id header, but ONLY outside
        // production - this header is client-supplied and trivially spoofable,
        // and is used solely by local/CI performance tests that have no
        // authenticated JWT to derive org_id from. This global query-filter
        // context is defense-in-depth, never the sole authorization check
        // (every controller separately validates membership/permission via
        // ICurrentOrganizationService or the relevant service layer) - but a
        // production request should never be able to steer it via a header.
        else if ((_environment.IsDevelopment() || _environment.EnvironmentName == "Local") &&
            context.Request.Headers.TryGetValue("X-Organization-Id", out var orgIdHeader) &&
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
