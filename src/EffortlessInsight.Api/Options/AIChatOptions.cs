namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for the AI Chat feature.
/// </summary>
public class AIChatOptions
{
    public const string SectionName = "AIChat";

    /// <summary>
    /// The AI provider to use: "openai", "anthropic", or "azure".
    /// </summary>
    public string Provider { get; set; } = "openai";

    /// <summary>
    /// OpenAI configuration.
    /// </summary>
    public OpenAIOptions OpenAI { get; set; } = new();

    /// <summary>
    /// Anthropic configuration.
    /// </summary>
    public AnthropicOptions Anthropic { get; set; } = new();

    /// <summary>
    /// Azure OpenAI configuration.
    /// </summary>
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();

    /// <summary>
    /// Conversation management settings.
    /// </summary>
    public ConversationOptions Conversation { get; set; } = new();

    /// <summary>
    /// Rate limiting settings.
    /// </summary>
    public RateLimitingOptions RateLimiting { get; set; } = new();
}

public class OpenAIOptions
{
    /// <summary>
    /// OpenAI API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default model to use (e.g., "gpt-4o", "gpt-4o-mini").
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Base URL for the OpenAI API.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    /// <summary>
    /// Maximum tokens to generate per request.
    /// </summary>
    public int MaxTokensPerRequest { get; set; } = 4096;

    /// <summary>
    /// Temperature for response generation (0.0-1.0).
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Resolves the API key from configuration, falling back to the
    /// OPENAI_API_KEY environment variable when not set in appsettings.
    /// </summary>
    public string ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(ApiKey)
            ? ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
}

public class AnthropicOptions
{
    /// <summary>
    /// Anthropic API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default model to use (e.g., "claude-3-sonnet-20240229").
    /// </summary>
    public string DefaultModel { get; set; } = "claude-3-sonnet-20240229";

    /// <summary>
    /// Maximum tokens to generate per request.
    /// </summary>
    public int MaxTokensPerRequest { get; set; } = 4096;
}

public class AzureOpenAIOptions
{
    /// <summary>
    /// Azure OpenAI endpoint URL.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for the model.
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// API version (e.g., "2024-02-01").
    /// </summary>
    public string ApiVersion { get; set; } = "2024-02-01";
}

public class ConversationOptions
{
    /// <summary>
    /// Maximum number of messages to include in context.
    /// </summary>
    public int MaxMessagesInContext { get; set; } = 20;

    /// <summary>
    /// Maximum tokens allowed for conversation context.
    /// </summary>
    public int MaxTokensForContext { get; set; } = 8000;

    /// <summary>
    /// Number of messages after which to create a summary.
    /// </summary>
    public int SummarizeAfterMessages { get; set; } = 50;

    /// <summary>
    /// Maximum conversations allowed per notice.
    /// </summary>
    public int MaxConversationsPerNotice { get; set; } = 10;
}

public class RateLimitingOptions
{
    /// <summary>
    /// Maximum messages per minute per user.
    /// </summary>
    public int MessagesPerMinute { get; set; } = 10;

    /// <summary>
    /// Maximum messages per hour per user.
    /// </summary>
    public int MessagesPerHour { get; set; } = 100;
}
