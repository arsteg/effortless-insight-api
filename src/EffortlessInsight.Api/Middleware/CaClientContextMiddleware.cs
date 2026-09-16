using System.Security.Claims;

namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Middleware that handles CA client context.
/// When a CA user has selected a client, this middleware sets the tenant context
/// to the client's organization for proper data scoping.
/// </summary>
public class CaClientContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CaClientContextMiddleware> _logger;

    // CA management endpoints that don't require client context
    private static readonly HashSet<string> ClientManagementPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/ca/register",
        "/api/v1/ca/profile",
        "/api/v1/ca/dashboard",
        "/api/v1/ca/clients",
        "/api/v1/ca/invitations",
        "/api/v1/ca/context"
    };

    public CaClientContextMiddleware(
        RequestDelegate next,
        ILogger<CaClientContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, Services.ITenantContext tenantContext)
    {
        var path = context.Request.Path.Value;

        // Skip for non-CA paths
        if (path == null || !path.StartsWith("/api/v1/ca/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var user = context.User;

        // Skip if not authenticated
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        // Check if this is a CA user
        var isCaClaim = user.FindFirst("is_ca")?.Value;
        if (isCaClaim != "true" && !IsCaPath(path))
        {
            await _next(context);
            return;
        }

        // Check if client context is required for this path
        if (!IsClientContextRequired(path))
        {
            await _next(context);
            return;
        }

        // Check if CA has selected a client
        var clientOrgId = user.FindFirst("ca_client_org_id")?.Value;
        if (string.IsNullOrEmpty(clientOrgId))
        {
            _logger.LogWarning("CA attempted to access client-scoped endpoint without selecting a client: {Path}", path);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                code = "CLIENT_NOT_SELECTED",
                message = "Please select a client before performing this action"
            });
            return;
        }

        // Set tenant context to client's organization
        if (Guid.TryParse(clientOrgId, out var orgId))
        {
            tenantContext.SetOrganizationId(orgId);
            _logger.LogDebug("CA context set to organization: {OrgId}", orgId);
        }

        await _next(context);
    }

    private static bool IsCaPath(string path)
    {
        return path.StartsWith("/api/v1/ca/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClientContextRequired(string path)
    {
        // Check if path starts with any of the management paths
        foreach (var managementPath in ClientManagementPaths)
        {
            if (path.StartsWith(managementPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // These paths require client context
        var requiresContext = new[]
        {
            "/api/v1/ca/notices",
            "/api/v1/ca/sync",
            "/api/v1/ca/gstins"
        };

        return requiresContext.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Extension methods for CA middleware registration.
/// </summary>
public static class CaClientContextMiddlewareExtensions
{
    /// <summary>
    /// Adds the CA client context middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseCaClientContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CaClientContextMiddleware>();
    }
}
