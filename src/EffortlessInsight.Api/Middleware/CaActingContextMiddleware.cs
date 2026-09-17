using EffortlessInsight.Api.Services.Ca;

namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Enforces that a CA acting on behalf of a client still has a live engagement.
///
/// Tenant scoping itself needs nothing here: a CA's token carries the client's org_id, so
/// TenantContextMiddleware already points the request at the right organization. What this
/// adds is revocation latency. Access tokens live 15 minutes, so without a per-request
/// re-check a CA would keep working for a quarter of an hour after the Business Owner
/// revoked them. This makes that take effect on the next request.
///
/// Costs nothing for ordinary users: it returns before any database work unless the token
/// actually carries a ca_client_rel_id claim.
/// </summary>
public class CaActingContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CaActingContextMiddleware> _logger;

    public CaActingContextMiddleware(
        RequestDelegate next,
        ILogger<CaActingContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICaActingContextService actingContextService)
    {
        if (context.User.FindFirst(CaClaimTypes.ClientRelationshipId) == null)
        {
            await _next(context);
            return;
        }

        var acting = await actingContextService.ResolveAsync(context.RequestAborted);
        if (acting == null)
        {
            _logger.LogWarning(
                "Rejecting request on a stale CA client context for {Path}", context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "CA_CONTEXT_INVALID",
                message = "Your access to this client has ended. Please select a client again."
            });
            return;
        }

        await _next(context);
    }
}

public static class CaActingContextMiddlewareExtensions
{
    /// <summary>
    /// Adds CA acting-context enforcement. Must run after authentication and after
    /// UseTenantContext, and before subscription enforcement.
    /// </summary>
    public static IApplicationBuilder UseCaActingContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CaActingContextMiddleware>();
    }
}
