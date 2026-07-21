using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EffortlessInsight.Api.Filters;

/// <summary>
/// Authentication filter for internal service-to-service API calls.
/// Validates X-Internal-Api-Key header against configured internal API key.
/// </summary>
public class InternalApiKeyAuthFilter : IAsyncActionFilter
{
    private const string ApiKeyHeaderName = "X-Internal-Api-Key";
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalApiKeyAuthFilter> _logger;

    public InternalApiKeyAuthFilter(
        IConfiguration configuration,
        ILogger<InternalApiKeyAuthFilter> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // Get configured API key
        var configuredApiKey = _configuration["InternalApi:ApiKey"];

        // Fail closed when unconfigured — no Development bypass. A misconfigured
        // environment must never expose an unauthenticated "send any notification
        // to any user" endpoint (audit BE-31).
        if (string.IsNullOrEmpty(configuredApiKey))
        {
            _logger.LogError("Internal API key not configured; rejecting request");
            context.Result = new UnauthorizedObjectResult(new
            {
                success = false,
                error = "Internal API not configured",
                code = "CONFIGURATION_ERROR"
            });
            return;
        }

        // Check for API key header
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedApiKey))
        {
            _logger.LogWarning(
                "Internal API request without API key from {IP}",
                context.HttpContext.Connection.RemoteIpAddress);

            context.Result = new UnauthorizedObjectResult(new
            {
                success = false,
                error = "API key required",
                code = "MISSING_API_KEY"
            });
            return;
        }

        // Validate API key with a constant-time comparison (audit BE-31)
        if (!FixedTimeEquals(configuredApiKey, providedApiKey.ToString()))
        {
            _logger.LogWarning(
                "Invalid internal API key from {IP}",
                context.HttpContext.Connection.RemoteIpAddress);

            context.Result = new UnauthorizedObjectResult(new
            {
                success = false,
                error = "Invalid API key",
                code = "INVALID_API_KEY"
            });
            return;
        }

        // API key is valid, proceed
        await next();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
