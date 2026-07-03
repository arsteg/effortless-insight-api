using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.AIChat.Providers;

/// <summary>
/// OpenAI API provider implementation.
/// </summary>
public class OpenAIProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProvider> _logger;
    private readonly AIChatOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ProviderId => "openai";
    public string DefaultModel => _options.OpenAI.DefaultModel;

    public OpenAIProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AIChatOptions> options,
        ILogger<OpenAIProvider> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OpenAI");
        _options = options.Value;
        _logger = logger;

        // Configure the client if not already configured
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_options.OpenAI.BaseUrl);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.OpenAI.ApiKey}");
        }
    }

    public async Task<AICompletionResult> CompleteAsync(
        AICompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var model = request.Model ?? DefaultModel;
            var messages = BuildMessages(request.SystemPrompt, request.Messages);

            var requestBody = new
            {
                model,
                messages,
                temperature = request.Temperature,
                max_tokens = request.MaxTokens,
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(
                "chat/completions",
                requestBody,
                JsonOptions,
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API error: {StatusCode} - {Content}",
                    response.StatusCode, responseContent);

                stopwatch.Stop();
                return new AICompletionResult(
                    Success: false,
                    Content: string.Empty,
                    InputTokens: 0,
                    OutputTokens: 0,
                    TotalTokens: 0,
                    Model: model,
                    ResponseTimeMs: (int)stopwatch.ElapsedMilliseconds,
                    ErrorCode: response.StatusCode.ToString(),
                    ErrorMessage: responseContent
                );
            }

            var result = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, JsonOptions);

            stopwatch.Stop();

            return new AICompletionResult(
                Success: true,
                Content: result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty,
                InputTokens: result?.Usage?.PromptTokens ?? 0,
                OutputTokens: result?.Usage?.CompletionTokens ?? 0,
                TotalTokens: result?.Usage?.TotalTokens ?? 0,
                Model: model,
                ResponseTimeMs: (int)stopwatch.ElapsedMilliseconds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI completion failed");
            stopwatch.Stop();

            return new AICompletionResult(
                Success: false,
                Content: string.Empty,
                InputTokens: 0,
                OutputTokens: 0,
                TotalTokens: 0,
                Model: request.Model ?? DefaultModel,
                ResponseTimeMs: (int)stopwatch.ElapsedMilliseconds,
                ErrorCode: "PROVIDER_ERROR",
                ErrorMessage: ex.Message
            );
        }
    }

    public async IAsyncEnumerable<AIStreamChunk> StreamCompletionAsync(
        AICompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = request.Model ?? DefaultModel;
        var messages = BuildMessages(request.SystemPrompt, request.Messages);

        var requestBody = new
        {
            model,
            messages,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            stream = true
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions)
        };

        HttpResponseMessage? response = null;

        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("OpenAI streaming error: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                yield return new AIStreamChunk(
                    string.Empty,
                    IsComplete: true,
                    ErrorMessage: $"API error: {response.StatusCode}"
                );
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);

                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                    continue;

                var data = line[6..]; // Remove "data: " prefix

                if (data == "[DONE]")
                {
                    yield return new AIStreamChunk(string.Empty, IsComplete: true);
                    yield break;
                }

                OpenAIStreamChunk? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse stream chunk: {Data}", data);
                    continue;
                }

                var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;

                if (!string.IsNullOrEmpty(content))
                {
                    yield return new AIStreamChunk(content, IsComplete: false);
                }
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    public int EstimateTokenCount(string text)
    {
        // Rough estimate: ~4 chars per token for English
        // This is a simplification; for accurate counts, use tiktoken
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static List<object> BuildMessages(string systemPrompt, List<AIMessage> messages)
    {
        var result = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var msg in messages)
        {
            result.Add(new { role = msg.Role, content = msg.Content });
        }

        return result;
    }
}

// OpenAI Response DTOs
internal class OpenAIResponse
{
    public string? Id { get; set; }
    public List<OpenAIChoice>? Choices { get; set; }
    public OpenAIUsage? Usage { get; set; }
}

internal class OpenAIChoice
{
    public OpenAIMessage? Message { get; set; }
    public int? Index { get; set; }
    public string? FinishReason { get; set; }
}

internal class OpenAIMessage
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

internal class OpenAIUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

internal class OpenAIStreamChunk
{
    public string? Id { get; set; }
    public List<OpenAIStreamChoice>? Choices { get; set; }
}

internal class OpenAIStreamChoice
{
    public OpenAIDelta? Delta { get; set; }
    public int? Index { get; set; }
    public string? FinishReason { get; set; }
}

internal class OpenAIDelta
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}
