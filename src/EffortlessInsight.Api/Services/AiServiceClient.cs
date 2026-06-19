using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services;

/// <summary>
/// HTTP client for communicating with the Python AI Service.
/// Handles notice processing, response generation, and similarity search.
/// </summary>
public class AiServiceClientImpl : IAiServiceClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiServiceClientImpl> _logger;
    private readonly AiServiceOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AiServiceClientImpl(
        IHttpClientFactory httpClientFactory,
        IOptions<AiServiceOptions> options,
        ILogger<AiServiceClientImpl> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Process a notice through the AI pipeline.
    /// </summary>
    /// <param name="noticeId">The notice ID to process.</param>
    /// <param name="fileUrl">Presigned S3 URL for the notice file (30-min validity).</param>
    /// <returns>AI processing result with report data.</returns>
    public async Task<AiProcessingResult> ProcessNoticeAsync(Guid noticeId, string fileUrl)
    {
        _logger.LogInformation("Sending notice {NoticeId} to AI service for processing", noticeId);

        var request = new ProcessNoticeRequest
        {
            NoticeId = noticeId,
            FileUrl = fileUrl,
            Priority = "normal"
        };

        try
        {
            var response = await ExecuteWithRetryAsync<ProcessNoticeRequest, AiProcessingResponse>(
                HttpMethod.Post,
                "/api/v1/process/notice",
                request);

            if (response == null)
            {
                _logger.LogError("AI service returned null response for notice {NoticeId}", noticeId);
                return new AiProcessingResult(false, "AI service returned empty response", null);
            }

            _logger.LogDebug(
                "AI service response for notice {NoticeId}: Success={Success}, HasReport={HasReport}, Error={Error}",
                noticeId, response.Success, response.Report != null, response.Error);

            if (!response.Success)
            {
                _logger.LogWarning(
                    "AI processing failed for notice {NoticeId}: {Error}",
                    noticeId, response.Error);
                return new AiProcessingResult(false, response.Error, null);
            }

            if (response.Report == null)
            {
                _logger.LogError("AI service returned success but report is null for notice {NoticeId}", noticeId);
                return new AiProcessingResult(false, "AI service returned success but no report data", null);
            }

            var report = MapToAiReportData(response.Report);

            _logger.LogInformation(
                "AI processing completed for notice {NoticeId}. Risk: {RiskLevel} ({RiskScore})",
                noticeId, report?.RiskLevel, report?.RiskScore);

            return new AiProcessingResult(true, null, report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception calling AI service for notice {NoticeId}", noticeId);
            return new AiProcessingResult(false, $"AI service error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Generate a draft response for a notice.
    /// </summary>
    /// <param name="noticeId">The notice ID to generate a response for.</param>
    /// <returns>Generated draft response text.</returns>
    public async Task<string> GenerateResponseDraftAsync(Guid noticeId)
    {
        _logger.LogInformation("Requesting response draft for notice {NoticeId}", noticeId);

        var request = new GenerateResponseRequest
        {
            NoticeId = noticeId,
            Tone = "formal",
            IncludeCaseLaw = true
        };

        try
        {
            var response = await ExecuteWithRetryAsync<GenerateResponseRequest, GenerateResponseResponse>(
                HttpMethod.Post,
                "/api/v1/process/generate-response",
                request);

            if (response == null || !response.Success)
            {
                var error = response?.Error ?? "Empty response from AI service";
                _logger.LogWarning("Response generation failed for notice {NoticeId}: {Error}", noticeId, error);
                throw new InvalidOperationException($"Failed to generate response: {error}");
            }

            _logger.LogInformation("Response draft generated for notice {NoticeId}", noticeId);
            return response.Draft ?? string.Empty;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Exception generating response for notice {NoticeId}", noticeId);
            throw new InvalidOperationException($"Failed to generate response: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Find similar notices using vector similarity search.
    /// </summary>
    /// <param name="noticeId">The notice ID to find similar notices for.</param>
    /// <param name="limit">Maximum number of similar notices to return.</param>
    /// <returns>List of similar notices with similarity scores.</returns>
    public async Task<List<SimilarNotice>> FindSimilarNoticesAsync(Guid noticeId, int limit = 5)
    {
        _logger.LogInformation("Finding similar notices for {NoticeId} (limit: {Limit})", noticeId, limit);

        var request = new SimilarNoticesRequest
        {
            NoticeId = noticeId,
            Limit = Math.Clamp(limit, 1, 20)
        };

        try
        {
            var response = await ExecuteWithRetryAsync<SimilarNoticesRequest, SimilarNoticesResponse>(
                HttpMethod.Post,
                "/api/v1/process/similar",
                request);

            if (response == null || !response.Success)
            {
                _logger.LogWarning("Similar notice search failed for {NoticeId}", noticeId);
                return [];
            }

            var results = response.SimilarNotices?
                .Select(s => new SimilarNotice(
                    s.NoticeId,
                    s.SimilarityScore,
                    s.NoticeType,
                    s.Summary))
                .ToList() ?? [];

            _logger.LogInformation("Found {Count} similar notices for {NoticeId}", results.Count, noticeId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception finding similar notices for {NoticeId}", noticeId);
            return [];
        }
    }

    /// <summary>
    /// Execute an HTTP request with exponential backoff retry logic.
    /// </summary>
    private async Task<TResponse?> ExecuteWithRetryAsync<TRequest, TResponse>(
        HttpMethod method,
        string endpoint,
        TRequest request)
        where TResponse : class
    {
        var client = _httpClientFactory.CreateClient("AiService");
        var attempts = 0;
        Exception? lastException = null;

        while (attempts < _options.MaxRetries)
        {
            attempts++;

            try
            {
                using var httpRequest = new HttpRequestMessage(method, endpoint);
                httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

                // Add API key header if configured
                if (!string.IsNullOrEmpty(_options.ApiKey))
                {
                    httpRequest.Headers.Add("X-API-Key", _options.ApiKey);
                }

                using var response = await client.SendAsync(httpRequest);

                if (response.IsSuccessStatusCode)
                {
                    // Read raw content for debugging
                    var rawContent = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("AI service raw response: {Response}", rawContent.Length > 500 ? rawContent[..500] + "..." : rawContent);

                    try
                    {
                        var result = JsonSerializer.Deserialize<TResponse>(rawContent, JsonOptions);
                        if (result == null)
                        {
                            _logger.LogWarning("Deserialization returned null for response: {Response}", rawContent[..Math.Min(500, rawContent.Length)]);
                        }
                        return result;
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Failed to deserialize AI response: {Response}", rawContent[..Math.Min(1000, rawContent.Length)]);
                        throw;
                    }
                }

                // Handle specific error codes
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("AI service returned 400 Bad Request: {Error}", errorContent);
                    throw new InvalidOperationException($"Bad request: {errorContent}");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("AI service endpoint not found: {Endpoint}", endpoint);
                    throw new InvalidOperationException($"Endpoint not found: {endpoint}");
                }

                // Retry on server errors or rate limiting
                if (IsTransientError(response.StatusCode))
                {
                    _logger.LogWarning(
                        "AI service returned {StatusCode} (attempt {Attempt}/{MaxRetries})",
                        response.StatusCode, attempts, _options.MaxRetries);

                    if (attempts < _options.MaxRetries)
                    {
                        await DelayWithBackoffAsync(attempts);
                        continue;
                    }
                }

                // Non-retryable error
                var content = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"AI service returned {response.StatusCode}: {content}");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogWarning(
                    "AI service request timed out (attempt {Attempt}/{MaxRetries})",
                    attempts, _options.MaxRetries);
                lastException = ex;

                if (attempts < _options.MaxRetries)
                {
                    await DelayWithBackoffAsync(attempts);
                    continue;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "AI service request failed (attempt {Attempt}/{MaxRetries})",
                    attempts, _options.MaxRetries);
                lastException = ex;

                if (attempts < _options.MaxRetries)
                {
                    await DelayWithBackoffAsync(attempts);
                    continue;
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Unexpected error calling AI service");
                lastException = ex;

                if (attempts < _options.MaxRetries)
                {
                    await DelayWithBackoffAsync(attempts);
                    continue;
                }
            }
        }

        if (lastException != null)
        {
            throw lastException;
        }

        return null;
    }

    private static bool IsTransientError(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout or
            HttpStatusCode.TooManyRequests;
    }

    private async Task DelayWithBackoffAsync(int attempt)
    {
        // Exponential backoff: 2s, 4s, 8s, etc. with jitter
        var baseDelay = _options.RetryDelaySeconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * 0.3 * baseDelay; // 0-30% jitter
        var delay = TimeSpan.FromSeconds(baseDelay + jitter);

        _logger.LogDebug("Retrying after {Delay}", delay);
        await Task.Delay(delay);
    }

    private static AiReportData? MapToAiReportData(AiReportResponse? response)
    {
        if (response == null) return null;

        return new AiReportData(
            response.RiskScore,
            response.RiskLevel ?? "unknown",
            response.SummaryEn ?? string.Empty,
            response.SummaryHi ?? string.Empty,
            response.PlainEnglish ?? string.Empty,
            new NoticeMetadata(
                response.Metadata?.NoticeType,
                response.Metadata?.NoticeCategory,
                response.Metadata?.NoticeNumber,
                response.Metadata?.Gstin,
                response.Metadata?.IssueDate,
                response.Metadata?.ResponseDeadline,
                response.Metadata?.TaxAmount,
                response.Metadata?.PenaltyAmount,
                response.Metadata?.InterestAmount,
                response.Metadata?.PeriodFrom,
                response.Metadata?.PeriodTo,
                response.Metadata?.IssuingAuthority
            ),
            response.ActionItems?.Select(a => new ActionItemDto(
                a.Priority,
                a.Action ?? string.Empty,
                a.Description ?? string.Empty,
                a.DueInDays,
                a.AssigneeSuggestion
            )).ToList() ?? [],
            response.RequiredDocuments?.Select(d => new RequiredDocumentDto(
                d.Document ?? string.Empty,
                d.Mandatory
            )).ToList() ?? [],
            response.LegalReferences?.Select(l => new LegalReferenceDto(
                l.Section ?? string.Empty,
                l.Description ?? string.Empty
            )).ToList() ?? [],
            response.ConfidenceScores ?? new Dictionary<string, int>()
        );
    }

    #region Request/Response DTOs for AI Service

    private record ProcessNoticeRequest
    {
        public Guid NoticeId { get; init; }
        public string FileUrl { get; init; } = string.Empty;
        public Guid? OrganizationId { get; init; }
        public string Priority { get; init; } = "normal";
    }

    private record GenerateResponseRequest
    {
        public Guid NoticeId { get; init; }
        public Dictionary<string, object>? Context { get; init; }
        public string Tone { get; init; } = "formal";
        public bool IncludeCaseLaw { get; init; } = true;
    }

    private record SimilarNoticesRequest
    {
        public Guid NoticeId { get; init; }
        public Guid? OrganizationId { get; init; }
        public int Limit { get; init; } = 5;
    }

    private record AiProcessingResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public AiReportResponse? Report { get; init; }
    }

    private record AiReportResponse
    {
        public int RiskScore { get; init; }
        public string? RiskLevel { get; init; }
        public string? SummaryEn { get; init; }
        public string? SummaryHi { get; init; }
        public string? PlainEnglish { get; init; }
        public NoticeMetadataResponse? Metadata { get; init; }
        public List<ActionItemResponse>? ActionItems { get; init; }
        public List<RequiredDocumentResponse>? RequiredDocuments { get; init; }
        public List<LegalReferenceResponse>? LegalReferences { get; init; }
        public Dictionary<string, int>? ConfidenceScores { get; init; }
    }

    private record NoticeMetadataResponse
    {
        public string? NoticeType { get; init; }
        public string? NoticeCategory { get; init; }
        public string? NoticeNumber { get; init; }
        public string? Gstin { get; init; }
        public DateOnly? IssueDate { get; init; }
        public DateOnly? ResponseDeadline { get; init; }
        public decimal? TaxAmount { get; init; }
        public decimal? PenaltyAmount { get; init; }
        public decimal? InterestAmount { get; init; }
        public DateOnly? PeriodFrom { get; init; }
        public DateOnly? PeriodTo { get; init; }
        public string? IssuingAuthority { get; init; }
    }

    private record ActionItemResponse
    {
        public int Priority { get; init; }
        public string? Action { get; init; }
        public string? Description { get; init; }
        public int? DueInDays { get; init; }
        public string? AssigneeSuggestion { get; init; }
    }

    private record RequiredDocumentResponse
    {
        public string? Document { get; init; }
        public bool Mandatory { get; init; }
    }

    private record LegalReferenceResponse
    {
        public string? Section { get; init; }
        public string? Description { get; init; }
    }

    private record GenerateResponseResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public string? Draft { get; init; }
    }

    private record SimilarNoticesResponse
    {
        public bool Success { get; init; }
        public Guid NoticeId { get; init; }
        public List<SimilarNoticeResponse>? SimilarNotices { get; init; }
    }

    private record SimilarNoticeResponse
    {
        public Guid NoticeId { get; init; }
        public float SimilarityScore { get; init; }
        public string? NoticeType { get; init; }
        public string? Summary { get; init; }
    }

    #endregion
}
