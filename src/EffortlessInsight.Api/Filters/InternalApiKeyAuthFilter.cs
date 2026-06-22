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

        // In development, if no key is configured, allow requests (with warning)
        if (string.IsNullOrEmpty(configuredApiKey))
        {
            var environment = _configuration["ASPNETCORE_ENVIRONMENT"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            if (environment == "Development")
            {
                _logger.LogWarning(
                    "Internal API key not configured. Allowing request in Development mode. " +
                    "Configure 'InternalApi:ApiKey' for production.");
                await next();
                return;
            }

            _logger.LogError("Internal API key not configured in production");
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

        // Validate API key
        if (!string.Equals(configuredApiKey, providedApiKey.ToString(), StringComparison.Ordinal))
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
}
