namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Utility for token estimation and management.
/// </summary>
public static class TokenManager
{
    /// <summary>
    /// Average characters per token (rough estimate for English text).
    /// GPT models average around 4 characters per token for English.
    /// </summary>
    private const double CHARS_PER_TOKEN = 4.0;

    /// <summary>
    /// Token overhead for message formatting (role, separators, etc.).
    /// </summary>
    private const int MESSAGE_OVERHEAD_TOKENS = 4;

    /// <summary>
    /// Token limits for different models.
    /// </summary>
    public static readonly Dictionary<string, ModelTokenLimits> ModelLimits = new()
    {
        ["gpt-4o"] = new ModelTokenLimits(128000, 16384, 0.005m, 0.015m),
        ["gpt-4o-mini"] = new ModelTokenLimits(128000, 16384, 0.00015m, 0.0006m),
        ["gpt-4-turbo"] = new ModelTokenLimits(128000, 4096, 0.01m, 0.03m),
        ["gpt-4"] = new ModelTokenLimits(8192, 8192, 0.03m, 0.06m),
        ["gpt-3.5-turbo"] = new ModelTokenLimits(16385, 4096, 0.0005m, 0.0015m),
        ["claude-3-opus"] = new ModelTokenLimits(200000, 4096, 0.015m, 0.075m),
        ["claude-3-sonnet"] = new ModelTokenLimits(200000, 4096, 0.003m, 0.015m),
        ["claude-3-haiku"] = new ModelTokenLimits(200000, 4096, 0.00025m, 0.00125m),
    };

    /// <summary>
    /// Estimate token count for a piece of text.
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / CHARS_PER_TOKEN);
    }

    /// <summary>
    /// Estimate token count for a message including overhead.
    /// </summary>
    public static int EstimateMessageTokens(string role, string content)
    {
        return EstimateTokens(content) + MESSAGE_OVERHEAD_TOKENS;
    }

    /// <summary>
    /// Estimate total tokens for a conversation.
    /// </summary>
    public static int EstimateConversationTokens(string systemPrompt, IEnumerable<(string Role, string Content)> messages)
    {
        var total = EstimateTokens(systemPrompt) + MESSAGE_OVERHEAD_TOKENS;

        foreach (var (role, content) in messages)
        {
            total += EstimateMessageTokens(role, content);
        }

        return total;
    }

    /// <summary>
    /// Calculate the available tokens for response given context usage.
    /// </summary>
    public static int CalculateAvailableResponseTokens(string model, int contextTokens)
    {
        if (!ModelLimits.TryGetValue(model, out var limits))
        {
            // Default to conservative limits
            return Math.Max(1000, 8192 - contextTokens);
        }

        // Reserve space for response, up to max output
        var available = limits.ContextWindow - contextTokens;
        return Math.Min(available, limits.MaxOutput);
    }

    /// <summary>
    /// Calculate estimated cost for a request.
    /// </summary>
    public static decimal EstimateCost(string model, int inputTokens, int outputTokens)
    {
        if (!ModelLimits.TryGetValue(model, out var limits))
        {
            // Default to GPT-4 pricing
            return (inputTokens / 1000m * 0.01m) + (outputTokens / 1000m * 0.03m);
        }

        return (inputTokens / 1000m * limits.InputCostPer1K) +
               (outputTokens / 1000m * limits.OutputCostPer1K);
    }

    /// <summary>
    /// Truncate text to fit within a token budget.
    /// </summary>
    public static string TruncateToTokens(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var currentTokens = EstimateTokens(text);
        if (currentTokens <= maxTokens)
            return text;

        // Calculate approximate character limit
        var maxChars = (int)(maxTokens * CHARS_PER_TOKEN);

        // Try to truncate at a word boundary
        if (maxChars < text.Length)
        {
            var truncated = text[..maxChars];
            var lastSpace = truncated.LastIndexOf(' ');

            if (lastSpace > maxChars * 0.8) // Only use word boundary if it doesn't lose too much
            {
                truncated = truncated[..lastSpace];
            }

            return truncated + "...";
        }

        return text;
    }

    /// <summary>
    /// Check if a text fits within a token budget.
    /// </summary>
    public static bool FitsWithinBudget(string text, int tokenBudget)
    {
        return EstimateTokens(text) <= tokenBudget;
    }

    /// <summary>
    /// Get recommended context budget for a model.
    /// Leaves room for the response.
    /// </summary>
    public static int GetRecommendedContextBudget(string model, int reserveForResponse = 4096)
    {
        if (!ModelLimits.TryGetValue(model, out var limits))
        {
            return 4096; // Conservative default
        }

        return limits.ContextWindow - reserveForResponse;
    }
}

/// <summary>
/// Token limits and pricing for an AI model.
/// </summary>
public record ModelTokenLimits(
    int ContextWindow,
    int MaxOutput,
    decimal InputCostPer1K,
    decimal OutputCostPer1K
);
