using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.HealthChecks;

/// <summary>
/// Health check for WhatsApp integration.
/// Verifies configuration and API connectivity.
/// </summary>
public class WhatsAppHealthCheck : IHealthCheck
{
    private readonly MetaWhatsAppOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WhatsAppHealthCheck> _logger;

    public WhatsAppHealthCheck(
        IOptions<MetaWhatsAppOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppHealthCheck> logger)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["enabled"] = _options.Enabled
        };

        if (!_options.Enabled)
        {
            return HealthCheckResult.Healthy("WhatsApp integration is disabled", data);
        }

        // Check configuration
        if (string.IsNullOrEmpty(_options.AccessToken))
        {
            return HealthCheckResult.Unhealthy("WhatsApp access token not configured", data: data);
        }

        if (string.IsNullOrEmpty(_options.PhoneNumberId))
        {
            return HealthCheckResult.Unhealthy("WhatsApp phone number ID not configured", data: data);
        }

        try
        {
            // Test API connectivity by checking the phone number status
            var url = $"{_options.GraphApiBaseUrl}{_options.PhoneNumberId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {_options.AccessToken}");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                data["apiStatus"] = "connected";
                data["phoneNumberId"] = _options.PhoneNumberId;
                return HealthCheckResult.Healthy("WhatsApp API is reachable", data);
            }

            var statusCode = (int)response.StatusCode;
            data["apiStatusCode"] = statusCode;

            if (statusCode == 401)
            {
                return HealthCheckResult.Unhealthy("WhatsApp access token is invalid or expired", data: data);
            }

            if (statusCode >= 500)
            {
                return HealthCheckResult.Degraded("WhatsApp API is experiencing issues", data: data);
            }

            return HealthCheckResult.Degraded($"WhatsApp API returned status {statusCode}", data: data);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "WhatsApp health check failed: network error");
            data["error"] = ex.Message;
            return HealthCheckResult.Unhealthy("Cannot reach WhatsApp API", exception: ex, data: data);
        }
        catch (TaskCanceledException)
        {
            data["error"] = "Request timed out";
            return HealthCheckResult.Degraded("WhatsApp API request timed out", data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp health check failed unexpectedly");
            data["error"] = ex.Message;
            return HealthCheckResult.Unhealthy("WhatsApp health check failed", exception: ex, data: data);
        }
    }
}
