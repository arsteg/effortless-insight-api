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
    private readonly bool _allowHeaderOverride;

    public TenantContextMiddleware(
        RequestDelegate next,
        ILogger<TenantContextMiddleware> logger,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;

        // Off in production regardless; opt-in elsewhere so performance tests can still
        // drive the tenant context without a real login.
        _allowHeaderOverride = !environment.IsProduction()
            && configuration.GetValue("Tenancy:AllowHeaderOverride", true);

        if (_allowHeaderOverride)
        {
            _logger.LogWarning(
                "X-Organization-Id header override is enabled. This must never be on in production.");
        }
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
        // Fall back to the X-Organization-Id header (used by performance tests).
        //
        // Restricted to non-production because it is otherwise a tenant-escape primitive:
        // any caller whose token happens to carry no org_id could point the global query
        // filters at an arbitrary organization just by setting a header. Most endpoints
        // resolve their org from the claim rather than ITenantContext and so would still
        // refuse, but the filters are meant to be defence in depth, not the last line.
        else if (_allowHeaderOverride &&
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
