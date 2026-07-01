namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for GSP (GST Suvidha Provider) integrations.
/// </summary>
public class GspOptions
{
    public const string SectionName = "Gsp";

    /// <summary>
    /// WhiteBooks GSP configuration.
    /// </summary>
    public GspProviderOptions WhiteBooks { get; set; } = new();

    /// <summary>
    /// ClearTax GSP configuration.
    /// </summary>
    public GspProviderOptions ClearTax { get; set; } = new();

    /// <summary>
    /// Vayana GSP configuration.
    /// </summary>
    public GspProviderOptions Vayana { get; set; } = new();
}

/// <summary>
/// Configuration for a specific GSP provider.
/// </summary>
public class GspProviderOptions
{
    /// <summary>
    /// Base URL for the GSP API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Client ID for API authentication.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for API authentication.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// API key if required by the provider.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay in seconds for exponential backoff.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Whether this provider is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to use sandbox/test environment.
    /// </summary>
    public bool UseSandbox { get; set; } = false;

    /// <summary>
    /// Sandbox environment base URL.
    /// </summary>
    public string? SandboxBaseUrl { get; set; }

    /// <summary>
    /// Gets the effective base URL based on sandbox mode.
    /// </summary>
    public string EffectiveBaseUrl => UseSandbox && !string.IsNullOrEmpty(SandboxBaseUrl)
        ? SandboxBaseUrl
        : BaseUrl;
}
