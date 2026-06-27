using System.Text.Json;

namespace EffortlessInsight.Api.Services;

/// <summary>
/// Service for IP-based geolocation using ip-api.com (free tier).
/// </summary>
public interface IGeoLocationService
{
    /// <summary>
    /// Get location information for an IP address.
    /// </summary>
    Task<GeoLocationResult?> GetLocationAsync(string ipAddress);

    /// <summary>
    /// Lookup location information for an IP address.
    /// Alias for GetLocationAsync for interface compatibility.
    /// </summary>
    Task<GeoLocationResult?> LookupAsync(string ipAddress, CancellationToken ct = default);
}

public class GeoLocationService : IGeoLocationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeoLocationService> _logger;

    // Cache to avoid repeated lookups for same IP
    private static readonly Dictionary<string, (GeoLocationResult Result, DateTime CachedAt)> _cache = new();
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(24);
    private static readonly object _cacheLock = new();

    public GeoLocationService(IHttpClientFactory httpClientFactory, ILogger<GeoLocationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GeoLocation");
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<GeoLocationResult?> LookupAsync(string ipAddress, CancellationToken ct = default)
    {
        return GetLocationAsync(ipAddress);
    }

    public async Task<GeoLocationResult?> GetLocationAsync(string ipAddress)
    {
        // Skip lookup for localhost/private IPs
        if (IsPrivateOrLocalIp(ipAddress))
        {
            return new GeoLocationResult
            {
                City = "Local",
                Country = "Local Network",
                CountryCode = "LO",
                Success = true
            };
        }

        // Check cache first
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(ipAddress, out var cached) &&
                DateTime.UtcNow - cached.CachedAt < CacheExpiry)
            {
                return cached.Result;
            }
        }

        try
        {
            // ip-api.com free tier: http only, 45 requests/minute
            var response = await _httpClient.GetAsync($"http://ip-api.com/json/{ipAddress}?fields=status,message,country,countryCode,regionName,city,lat,lon,query");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GeoLocation API returned {StatusCode} for IP {IpAddress}",
                    response.StatusCode, ipAddress);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<IpApiResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data?.Status != "success")
            {
                _logger.LogWarning("GeoLocation lookup failed for IP {IpAddress}: {Message}",
                    ipAddress, data?.Message);
                return null;
            }

            var result = new GeoLocationResult
            {
                City = data.City,
                Country = data.Country,
                CountryCode = data.CountryCode,
                Region = data.RegionName,
                Latitude = data.Lat,
                Longitude = data.Lon,
                Success = true
            };

            // Cache the result
            lock (_cacheLock)
            {
                _cache[ipAddress] = (result, DateTime.UtcNow);

                // Cleanup old cache entries periodically
                if (_cache.Count > 1000)
                {
                    var expiredKeys = _cache
                        .Where(kvp => DateTime.UtcNow - kvp.Value.CachedAt > CacheExpiry)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in expiredKeys)
                    {
                        _cache.Remove(key);
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get geolocation for IP {IpAddress}", ipAddress);
            return null;
        }
    }

    private static bool IsPrivateOrLocalIp(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress) ||
            ipAddress == "unknown" ||
            ipAddress == "::1" ||
            ipAddress == "127.0.0.1" ||
            ipAddress.StartsWith("192.168.") ||
            ipAddress.StartsWith("10.") ||
            ipAddress.StartsWith("172.16.") ||
            ipAddress.StartsWith("172.17.") ||
            ipAddress.StartsWith("172.18.") ||
            ipAddress.StartsWith("172.19.") ||
            ipAddress.StartsWith("172.20.") ||
            ipAddress.StartsWith("172.21.") ||
            ipAddress.StartsWith("172.22.") ||
            ipAddress.StartsWith("172.23.") ||
            ipAddress.StartsWith("172.24.") ||
            ipAddress.StartsWith("172.25.") ||
            ipAddress.StartsWith("172.26.") ||
            ipAddress.StartsWith("172.27.") ||
            ipAddress.StartsWith("172.28.") ||
            ipAddress.StartsWith("172.29.") ||
            ipAddress.StartsWith("172.30.") ||
            ipAddress.StartsWith("172.31."))
        {
            return true;
        }

        return false;
    }
}

public class GeoLocationResult
{
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool Success { get; set; }

    /// <summary>
    /// Format location as "City, Country" or just country if city is unavailable.
    /// </summary>
    public string? GetFormattedLocation()
    {
        if (!Success) return null;

        if (!string.IsNullOrEmpty(City) && !string.IsNullOrEmpty(Country))
        {
            return $"{City}, {Country}";
        }

        return Country;
    }
}

/// <summary>
/// Response model for ip-api.com
/// </summary>
internal class IpApiResponse
{
    public string? Status { get; set; }
    public string? Message { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? RegionName { get; set; }
    public string? City { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public string? Query { get; set; }
}
