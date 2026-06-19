namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for the AI Service integration.
/// </summary>
public class AiServiceOptions
{
    public const string SectionName = "AiService";

    /// <summary>
    /// Base URL of the AI Service (e.g., http://localhost:8000).
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Timeout in seconds for processing requests (default: 120 seconds).
    /// AI processing can take significant time for large documents.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum number of retry attempts for transient failures.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay in seconds for exponential backoff between retries.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 2;

    /// <summary>
    /// API key for authenticating with the AI service (optional).
    /// </summary>
    public string? ApiKey { get; set; }
}
