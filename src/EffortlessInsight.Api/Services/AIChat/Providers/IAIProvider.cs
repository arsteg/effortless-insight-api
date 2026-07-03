using System.Runtime.CompilerServices;

namespace EffortlessInsight.Api.Services.AIChat.Providers;

/// <summary>
/// Abstraction for AI providers (OpenAI, Anthropic, Azure OpenAI, etc.).
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Unique identifier for this provider.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Default model to use if none specified.
    /// </summary>
    string DefaultModel { get; }

    /// <summary>
    /// Generate a complete response (non-streaming).
    /// </summary>
    Task<AICompletionResult> CompleteAsync(
        AICompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a streaming response.
    /// </summary>
    IAsyncEnumerable<AIStreamChunk> StreamCompletionAsync(
        AICompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate the token count for a given text.
    /// </summary>
    int EstimateTokenCount(string text);

    /// <summary>
    /// Check if the provider is available and configured.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for AI completion.
/// </summary>
public record AICompletionRequest(
    string SystemPrompt,
    List<AIMessage> Messages,
    string? Model = null,
    float Temperature = 0.3f,
    int MaxTokens = 4096
);

/// <summary>
/// Represents a message in the conversation history.
/// </summary>
public record AIMessage(string Role, string Content);

/// <summary>
/// Result of an AI completion request.
/// </summary>
public record AICompletionResult(
    bool Success,
    string Content,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    string Model,
    int ResponseTimeMs,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// A chunk of streamed AI response.
/// </summary>
public record AIStreamChunk(
    string Content,
    bool IsComplete,
    int? TotalTokens = null,
    string? ErrorMessage = null
);
