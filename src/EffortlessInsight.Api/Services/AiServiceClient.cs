using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services;

/// <summary>
/// HTTP client for communicating with the Python AI Service.
/// Handles notice processing, response generation, and similarity search.
/// </summary>
public class AiServiceClientImpl : IAiServiceClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISubscriptionService _subscriptionService;
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
        ISubscriptionService subscriptionService,
        IOptions<AiServiceOptions> options,
        ILogger<AiServiceClientImpl> logger)
    {
        _httpClientFactory = httpClientFactory;
        _subscriptionService = subscriptionService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Process a notice through the AI pipeline.
    /// </summary>
    /// <param name="noticeId">The notice ID to process.</param>
    /// <param name="fileUrl">Presigned S3 URL for the notice file (30-min validity).</param>
    /// <param name="organizationId">Owning organization — sent to the AI service for rate limiting / usage caps.</param>
    /// <returns>AI processing result with report data.</returns>
    public async Task<AiProcessingResult> ProcessNoticeAsync(Guid noticeId, string fileUrl, Guid organizationId)
    {
        _logger.LogInformation("Sending notice {NoticeId} to AI service for processing", noticeId);

        var request = new ProcessNoticeRequest
        {
            NoticeId = noticeId,
            FileUrl = fileUrl,
            OrganizationId = organizationId,
            Priority = "normal"
        };

        try
        {
            var response = await ExecuteWithRetryAsync<ProcessNoticeRequest, AiProcessingResponse>(
                HttpMethod.Post,
                "/api/v1/process/notice",
                request,
                organizationId);

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

            return new AiProcessingResult(true, null, report, response.RawText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception calling AI service for notice {NoticeId}", noticeId);
            return new AiProcessingResult(false, $"AI service error: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Generate a draft response for a notice (simple version).
    /// </summary>
    /// <param name="noticeId">The notice ID to generate a response for.</param>
    /// <param name="organizationId">Owning organization — sent to the AI service for rate limiting / usage caps.</param>
    /// <returns>Generated draft response text.</returns>
    public async Task<string> GenerateResponseDraftAsync(Guid noticeId, Guid organizationId)
    {
        var result = await GenerateResponseDraftAsync(noticeId, organizationId, new GenerateResponseOptions());
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to generate response: {result.Error}");
        }
        return result.Draft ?? string.Empty;
    }

    /// <summary>
    /// Generate a draft response for a notice with options.
    /// </summary>
    /// <param name="noticeId">The notice ID to generate a response for.</param>
    /// <param name="organizationId">Owning organization — sent to the AI service for rate limiting / usage caps.</param>
    /// <param name="options">Options for response generation.</param>
    /// <returns>Generated draft response with metadata.</returns>
    public async Task<GenerateResponseResult> GenerateResponseDraftAsync(Guid noticeId, Guid organizationId, GenerateResponseOptions options)
    {
        _logger.LogInformation(
            "Requesting response draft for notice {NoticeId}. Tone: {Tone}, Language: {Language}",
            noticeId, options.Tone, options.Language);

        var request = new GenerateResponseRequest
        {
            NoticeId = noticeId,
            Tone = options.Tone,
            Language = options.Language,
            PointsToAddress = options.PointsToAddress,
            AdditionalInstructions = options.AdditionalInstructions,
            Context = options.Context,
            IncludeCaseLaw = true
        };

        try
        {
            var response = await ExecuteWithRetryAsync<GenerateResponseRequest, GenerateResponseResponse>(
                HttpMethod.Post,
                "/api/v1/process/generate-response",
                request,
                organizationId);

            if (response == null)
            {
                _logger.LogWarning("Response generation returned null for notice {NoticeId}", noticeId);
                return new GenerateResponseResult
                {
                    Success = false,
                    Error = "Empty response from AI service"
                };
            }

            if (!response.Success)
            {
                _logger.LogWarning("Response generation failed for notice {NoticeId}: {Error}", noticeId, response.Error);
                return new GenerateResponseResult
                {
                    Success = false,
                    Error = response.Error ?? "Unknown error"
                };
            }

            _logger.LogInformation(
                "Response draft generated for notice {NoticeId}. Tokens: {InputTokens}/{OutputTokens}, Time: {Time}ms",
                noticeId, response.Metadata?.InputTokens, response.Metadata?.OutputTokens, response.Metadata?.ProcessingTimeMs);

            return new GenerateResponseResult
            {
                Success = true,
                Draft = response.Draft,
                Metadata = response.Metadata != null
                    ? new GenerateResponseMetadata
                    {
                        Model = response.Metadata.Model,
                        InputTokens = response.Metadata.InputTokens,
                        OutputTokens = response.Metadata.OutputTokens,
                        ProcessingTimeMs = response.Metadata.ProcessingTimeMs
                    }
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception generating response for notice {NoticeId}", noticeId);
            return new GenerateResponseResult
            {
                Success = false,
                Error = $"AI service error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Find similar notices using vector similarity search.
    /// </summary>
    /// <param name="noticeId">The notice ID to find similar notices for.</param>
    /// <param name="organizationId">Owning organization — sent to the AI service for rate limiting / usage caps.</param>
    /// <param name="limit">Maximum number of similar notices to return.</param>
    /// <returns>List of similar notices with similarity scores.</returns>
    public async Task<List<SimilarNotice>> FindSimilarNoticesAsync(Guid noticeId, Guid organizationId, int limit = 5)
    {
        _logger.LogInformation("Finding similar notices for {NoticeId} (limit: {Limit})", noticeId, limit);

        var request = new SimilarNoticesRequest
        {
            NoticeId = noticeId,
            OrganizationId = organizationId,
            Limit = Math.Clamp(limit, 1, 20)
        };

        try
        {
            var response = await ExecuteWithRetryAsync<SimilarNoticesRequest, SimilarNoticesResponse>(
                HttpMethod.Post,
                "/api/v1/process/similar",
                request,
                organizationId);

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
    /// Generate a handover summary for a notice case transfer.
    /// </summary>
    /// <param name="request">Request containing notice details and activity history.</param>
    /// <param name="organizationId">Owning organization — sent to the AI service for rate limiting.</param>
    /// <returns>Generated handover summary.</returns>
    public async Task<HandoverSummaryResult> GenerateHandoverSummaryAsync(HandoverSummaryRequest request, Guid organizationId)
    {
        _logger.LogInformation(
            "Generating handover summary for notice {NoticeId}",
            request.NoticeId);

        var aiRequest = new HandoverSummaryAiRequest
        {
            NoticeId = request.NoticeId,
            NoticeSummary = request.NoticeSummary,
            NoticeType = request.NoticeType,
            NoticeCategory = request.NoticeCategory,
            RiskLevel = request.RiskLevel,
            ResponseDeadline = request.ResponseDeadline,
            ActivityHistory = request.ActivityHistory?
                .Select(a => new Dictionary<string, string>
                {
                    ["action"] = a.Action,
                    ["user"] = a.User,
                    ["date"] = a.Date
                })
                .ToList(),
            CurrentStatus = request.CurrentStatus,
            AssignedTo = request.AssignedTo,
            AdditionalContext = request.AdditionalContext
        };

        try
        {
            var response = await ExecuteWithRetryAsync<HandoverSummaryAiRequest, HandoverSummaryAiResponse>(
                HttpMethod.Post,
                "/api/v1/process/handover-summary",
                aiRequest,
                organizationId);

            if (response == null)
            {
                _logger.LogWarning("Handover summary generation returned null for notice {NoticeId}", request.NoticeId);
                return new HandoverSummaryResult
                {
                    Success = false,
                    Error = "Empty response from AI service"
                };
            }

            if (!response.Success)
            {
                _logger.LogWarning("Handover summary generation failed for notice {NoticeId}: {Error}", request.NoticeId, response.Error);
                return new HandoverSummaryResult
                {
                    Success = false,
                    Error = response.Error ?? "Unknown error",
                    ProcessingTimeMs = response.ProcessingTimeMs
                };
            }

            _logger.LogInformation(
                "Handover summary generated for notice {NoticeId}. Time: {Time}ms",
                request.NoticeId, response.ProcessingTimeMs);

            return new HandoverSummaryResult
            {
                Success = true,
                Summary = response.Summary != null
                    ? new HandoverSummaryData
                    {
                        Background = response.Summary.Background ?? string.Empty,
                        WorkCompleted = response.Summary.WorkCompleted ?? [],
                        PendingItems = response.Summary.PendingItems ?? [],
                        NextSteps = response.Summary.NextSteps ?? [],
                        KeyDeadlines = response.Summary.KeyDeadlines?
                            .Select(d => new HandoverDeadline
                            {
                                Date = d.TryGetValue("date", out var date) ? date : string.Empty,
                                Description = d.TryGetValue("description", out var desc) ? desc : string.Empty
                            })
                            .ToList() ?? [],
                        KeyContacts = response.Summary.KeyContacts ?? [],
                        RiskSummary = response.Summary.RiskSummary,
                        GeneratedAt = response.Summary.GeneratedAt ?? DateTime.UtcNow
                    }
                    : null,
                ProcessingTimeMs = response.ProcessingTimeMs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception generating handover summary for notice {NoticeId}", request.NoticeId);
            return new HandoverSummaryResult
            {
                Success = false,
                Error = $"AI service error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Extract allegations from a notice.
    /// </summary>
    public async Task<AllegationExtractionResponse> ExtractAllegationsAsync(Guid noticeId, Guid organizationId, string? noticeText = null)
    {
        _logger.LogInformation("Extracting allegations from notice {NoticeId}", noticeId);

        var request = new ExtractAllegationsAiRequest
        {
            NoticeId = noticeId,
            NoticeText = noticeText
        };

        try
        {
            var response = await ExecuteWithRetryAsync<ExtractAllegationsAiRequest, AllegationExtractionAiResponse>(
                HttpMethod.Post,
                "/api/v1/process/extract-allegations",
                request,
                organizationId);

            if (response == null)
            {
                _logger.LogWarning("Allegation extraction returned null for notice {NoticeId}", noticeId);
                return new AllegationExtractionResponse
                {
                    Success = false,
                    Error = "Empty response from AI service"
                };
            }

            if (!response.Success)
            {
                _logger.LogWarning("Allegation extraction failed for notice {NoticeId}: {Error}", noticeId, response.Error);
                return new AllegationExtractionResponse
                {
                    Success = false,
                    Error = response.Error ?? "Unknown error"
                };
            }

            _logger.LogInformation(
                "Extracted {Count} allegations from notice {NoticeId}",
                response.Allegations?.Count ?? 0, noticeId);

            return new AllegationExtractionResponse
            {
                Success = true,
                Allegations = response.Allegations?
                    .Select(a => new AllegationData
                    {
                        Id = a.Id,
                        SerialNumber = a.SerialNumber,
                        Description = a.Description,
                        Category = a.Category,
                        LegalProvision = a.LegalProvision,
                        AmountInvolved = a.AmountInvolved,
                        Period = a.Period,
                        EvidenceCited = a.EvidenceCited,
                        PageReference = a.PageReference
                    }).ToList() ?? [],
                TotalAmount = response.TotalAmount,
                Metadata = response.Metadata != null
                    ? new GenerateResponseMetadata
                    {
                        Model = response.Metadata.Model,
                        InputTokens = response.Metadata.InputTokens,
                        OutputTokens = response.Metadata.OutputTokens,
                        ProcessingTimeMs = response.Metadata.ProcessingTimeMs
                    }
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception extracting allegations for notice {NoticeId}", noticeId);
            return new AllegationExtractionResponse
            {
                Success = false,
                Error = $"AI service error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Generate allegation-wise draft responses.
    /// </summary>
    public async Task<AllegationWiseDraftResult> GenerateAllegationWiseDraftAsync(Guid noticeId, Guid organizationId, AllegationWiseDraftOptions options)
    {
        _logger.LogInformation(
            "Generating allegation-wise draft for notice {NoticeId}. Tone: {Tone}, Language: {Language}",
            noticeId, options.Tone, options.Language);

        var request = new AllegationWiseDraftAiRequest
        {
            NoticeId = noticeId,
            NoticeText = options.NoticeText,
            Tone = options.Tone,
            Language = options.Language,
            Allegations = options.Allegations?.Select(a => new AllegationAiData
            {
                Id = a.Id,
                SerialNumber = a.SerialNumber,
                Description = a.Description,
                Category = a.Category,
                LegalProvision = a.LegalProvision,
                AmountInvolved = a.AmountInvolved,
                Period = a.Period,
                EvidenceCited = a.EvidenceCited
            }).ToList(),
            AllegationOverrides = options.AllegationOverrides,
            IncludeCaseLaw = options.IncludeCaseLaw,
            TaxpayerContext = options.TaxpayerContext
        };

        try
        {
            var response = await ExecuteWithRetryAsync<AllegationWiseDraftAiRequest, AllegationWiseDraftAiResponse>(
                HttpMethod.Post,
                "/api/v1/process/allegation-wise-draft",
                request,
                organizationId);

            if (response == null)
            {
                _logger.LogWarning("Allegation-wise draft generation returned null for notice {NoticeId}", noticeId);
                return new AllegationWiseDraftResult
                {
                    Success = false,
                    Error = "Empty response from AI service"
                };
            }

            if (!response.Success)
            {
                _logger.LogWarning("Allegation-wise draft generation failed for notice {NoticeId}: {Error}", noticeId, response.Error);
                return new AllegationWiseDraftResult
                {
                    Success = false,
                    Error = response.Error ?? "Unknown error"
                };
            }

            _logger.LogInformation(
                "Generated {Count} allegation responses for notice {NoticeId}",
                response.Responses?.Count ?? 0, noticeId);

            return new AllegationWiseDraftResult
            {
                Success = true,
                Allegations = response.Allegations?
                    .Select(a => new AllegationData
                    {
                        Id = a.Id,
                        SerialNumber = a.SerialNumber,
                        Description = a.Description,
                        Category = a.Category,
                        LegalProvision = a.LegalProvision,
                        AmountInvolved = a.AmountInvolved,
                        Period = a.Period,
                        EvidenceCited = a.EvidenceCited,
                        PageReference = a.PageReference
                    }).ToList() ?? [],
                Responses = response.Responses?
                    .Select(r => new AllegationResponseData
                    {
                        AllegationId = r.AllegationId,
                        ResponseText = r.ResponseText ?? string.Empty,
                        LegalBasis = r.LegalBasis ?? [],
                        SupportingEvidence = r.SupportingEvidence?
                            .Select(s => new SupportingDocumentData
                            {
                                DocumentName = s.DocumentName ?? string.Empty,
                                DocumentType = s.DocumentType ?? string.Empty,
                                Relevance = s.Relevance ?? string.Empty,
                                Available = s.Available
                            }).ToList() ?? [],
                        MissingInformation = r.MissingInformation ?? [],
                        CaseLawReferences = r.CaseLawReferences?
                            .Select(c => new CaseLawReferenceData
                            {
                                Citation = c.Citation ?? string.Empty,
                                Court = c.Court ?? string.Empty,
                                Principle = c.Principle ?? string.Empty,
                                Relevance = c.Relevance ?? string.Empty
                            }).ToList(),
                        Confidence = r.Confidence,
                        Status = r.Status ?? "needs_review"
                    }).ToList() ?? [],
                CombinedDraft = response.CombinedDraft,
                MissingInfoSummary = response.MissingInfoSummary ?? [],
                Metadata = response.Metadata != null
                    ? new GenerateResponseMetadata
                    {
                        Model = response.Metadata.Model,
                        InputTokens = response.Metadata.InputTokens,
                        OutputTokens = response.Metadata.OutputTokens,
                        ProcessingTimeMs = response.Metadata.ProcessingTimeMs
                    }
                    : null
            };
        }
        catch (AiUsageCapException ex)
        {
            return new AllegationWiseDraftResult { Success = false, ErrorCode = "USAGE_CAP_EXCEEDED", Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception generating allegation-wise draft for notice {NoticeId}", noticeId);
            return new AllegationWiseDraftResult
            {
                Success = false,
                Error = $"AI service error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Regenerate response for a single allegation.
    /// </summary>
    public async Task<AllegationResponseResult> RegenerateAllegationResponseAsync(Guid noticeId, Guid organizationId, RegenerateAllegationOptions options)
    {
        _logger.LogInformation(
            "Regenerating allegation response for notice {NoticeId}, allegation {AllegationId}",
            noticeId, options.Allegation.Id);

        var request = new RegenerateAllegationAiRequest
        {
            NoticeId = noticeId,
            Allegation = new AllegationAiData
            {
                Id = options.Allegation.Id,
                SerialNumber = options.Allegation.SerialNumber,
                Description = options.Allegation.Description,
                Category = options.Allegation.Category,
                LegalProvision = options.Allegation.LegalProvision,
                AmountInvolved = options.Allegation.AmountInvolved,
                Period = options.Allegation.Period,
                EvidenceCited = options.Allegation.EvidenceCited
            },
            Tone = options.Tone,
            Language = options.Language,
            CustomInstructions = options.CustomInstructions,
            TaxpayerContext = options.TaxpayerContext
        };

        try
        {
            var response = await ExecuteWithRetryAsync<RegenerateAllegationAiRequest, AllegationResponseAiResponse>(
                HttpMethod.Post,
                "/api/v1/process/regenerate-allegation-response",
                request,
                organizationId);

            if (response == null || string.IsNullOrWhiteSpace(response.ResponseText) || response.AllegationId != options.Allegation.Id)
            {
                _logger.LogWarning("Allegation response regeneration returned null for notice {NoticeId}", noticeId);
                return new AllegationResponseResult
                {
                    Success = false,
                    Error = "Empty response from AI service"
                };
            }

            _logger.LogInformation(
                "Regenerated allegation response for notice {NoticeId}, allegation {AllegationId}. Confidence: {Confidence}",
                noticeId, options.Allegation.Id, response.Confidence);

            return new AllegationResponseResult
            {
                Success = true,
                Response = new AllegationResponseData
                {
                    AllegationId = response.AllegationId ?? options.Allegation.Id,
                    ResponseText = response.ResponseText ?? string.Empty,
                    LegalBasis = response.LegalBasis ?? [],
                    SupportingEvidence = response.SupportingEvidence?
                        .Select(s => new SupportingDocumentData
                        {
                            DocumentName = s.DocumentName ?? string.Empty,
                            DocumentType = s.DocumentType ?? string.Empty,
                            Relevance = s.Relevance ?? string.Empty,
                            Available = s.Available
                        }).ToList() ?? [],
                    MissingInformation = response.MissingInformation ?? [],
                    CaseLawReferences = response.CaseLawReferences?
                        .Select(c => new CaseLawReferenceData
                        {
                            Citation = c.Citation ?? string.Empty,
                            Court = c.Court ?? string.Empty,
                            Principle = c.Principle ?? string.Empty,
                            Relevance = c.Relevance ?? string.Empty
                        }).ToList(),
                    Confidence = response.Confidence,
                    Status = response.Status ?? "needs_review"
                }
            };
        }
        catch (AiUsageCapException ex)
        {
            return new AllegationResponseResult { Success = false, ErrorCode = "USAGE_CAP_EXCEEDED", Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception regenerating allegation response for notice {NoticeId}", noticeId);
            return new AllegationResponseResult
            {
                Success = false,
                Error = $"AI service error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Execute an HTTP request with exponential backoff retry logic.
    /// </summary>
    private async Task<TResponse?> ExecuteWithRetryAsync<TRequest, TResponse>(
        HttpMethod method,
        string endpoint,
        TRequest request,
        Guid organizationId)
        where TResponse : class
    {
        var client = _httpClientFactory.CreateClient("AiService");

        // Resolve the caller's plan once (not per retry) so the AI service can
        // apply per-plan usage caps. Best-effort: a lookup failure just omits
        // the header and the AI service falls back to its default cap.
        var planCode = await ResolvePlanCodeAsync(organizationId);

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

                // Identify the caller so the AI service can rate-limit and apply
                // per-plan monthly usage caps (see RateLimitMiddleware).
                if (organizationId != Guid.Empty)
                {
                    httpRequest.Headers.Add("X-Organization-Id", organizationId.ToString());
                }
                if (!string.IsNullOrEmpty(planCode))
                {
                    httpRequest.Headers.Add("X-Plan", planCode);
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

                // A monthly quota cannot recover after a short retry delay.
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var quotaContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        using var payload = JsonDocument.Parse(quotaContent);
                        if (payload.RootElement.TryGetProperty("code", out var code)
                            && code.GetString() == "USAGE_CAP_EXCEEDED")
                        {
                            var message = payload.RootElement.TryGetProperty("error", out var error)
                                ? error.GetString() : null;
                            throw new AiUsageCapException(message ?? "Monthly AI usage limit reached.");
                        }
                    }
                    catch (JsonException) { /* Non-JSON 429s retain transient retry behavior. */ }
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

    /// <summary>
    /// Resolves the organization's current plan code (e.g. "free", "starter")
    /// for the X-Plan header. Returns null if the org has no subscription or the
    /// lookup fails — the AI service then applies its default cap.
    /// </summary>
    private async Task<string?> ResolvePlanCodeAsync(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var subscription = await _subscriptionService.GetSubscriptionEntityAsync(organizationId);
            return subscription?.PlanCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not resolve plan for organization {OrganizationId}; AI service will use its default cap",
                organizationId);
            return null;
        }
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

        // Map risk assessment
        RiskAssessmentDto? riskAssessment = null;
        if (response.RiskAssessment != null)
        {
            riskAssessment = new RiskAssessmentDto(
                response.RiskAssessment.Score,
                response.RiskAssessment.Level,
                response.RiskAssessment.NoticeCategoryScore,
                response.RiskAssessment.TaxAmountScore,
                response.RiskAssessment.DeadlineUrgencyScore,
                response.RiskAssessment.PenaltyPotentialScore,
                response.RiskAssessment.PriorHistoryScore,
                response.RiskAssessment.ComplexityScore,
                response.RiskAssessment.Reasoning
            );
        }

        // Map calculation discrepancies
        var discrepancies = response.CalculationDiscrepancies?.Select(d => new CalculationDiscrepancyDto(
            d.Field,
            d.Expected,
            d.Actual,
            d.Difference,
            d.Message,
            d.Severity
        )).ToList();

        // Map prefiling checklist
        var checklist = response.PrefilingChecklist?.Select(c => new PrefilingChecklistItemDto(
            c.Category,
            c.Item,
            c.Status,
            c.Details,
            c.Priority
        )).ToList();

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
                response.Metadata?.LateFeeAmount,
                response.Metadata?.CessAmount,
                response.Metadata?.OtherChargesAmount,
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
            response.ConfidenceScores ?? new Dictionary<string, int>(),
            response.RiskFactors,
            riskAssessment,
            discrepancies,
            checklist
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
        public string Language { get; init; } = "en";
        public bool IncludeCaseLaw { get; init; } = true;
        public List<string>? PointsToAddress { get; init; }
        public string? AdditionalInstructions { get; init; }
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
        public string? RawText { get; init; }
    }

    private record AiReportResponse
    {
        public int RiskScore { get; init; }
        public string? RiskLevel { get; init; }
        public List<string>? RiskFactors { get; init; }
        public RiskAssessmentResponse? RiskAssessment { get; init; }
        public string? SummaryEn { get; init; }
        public string? SummaryHi { get; init; }
        public string? PlainEnglish { get; init; }
        public NoticeMetadataResponse? Metadata { get; init; }
        public List<ActionItemResponse>? ActionItems { get; init; }
        public List<RequiredDocumentResponse>? RequiredDocuments { get; init; }
        public List<LegalReferenceResponse>? LegalReferences { get; init; }
        public Dictionary<string, int>? ConfidenceScores { get; init; }
        public List<CalculationDiscrepancyResponse>? CalculationDiscrepancies { get; init; }
        public List<PrefilingChecklistItemResponse>? PrefilingChecklist { get; init; }
    }

    private record RiskAssessmentResponse
    {
        public int Score { get; init; }
        public string Level { get; init; } = "medium";
        public int NoticeCategoryScore { get; init; }
        public int TaxAmountScore { get; init; }
        public int DeadlineUrgencyScore { get; init; }
        public int PenaltyPotentialScore { get; init; }
        public int PriorHistoryScore { get; init; }
        public int ComplexityScore { get; init; }
        public List<string>? Reasoning { get; init; }
    }

    private record CalculationDiscrepancyResponse
    {
        public string Field { get; init; } = string.Empty;
        public decimal Expected { get; init; }
        public decimal Actual { get; init; }
        public decimal Difference { get; init; }
        public string Message { get; init; } = string.Empty;
        public string Severity { get; init; } = "warning";
    }

    private record PrefilingChecklistItemResponse
    {
        public string Category { get; init; } = string.Empty;
        public string Item { get; init; } = string.Empty;
        public string Status { get; init; } = "unknown";
        public string? Details { get; init; }
        public int Priority { get; init; } = 2;
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
        public decimal? LateFeeAmount { get; init; }
        public decimal? CessAmount { get; init; }
        public decimal? OtherChargesAmount { get; init; }
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
        public GenerateResponseMetadataResponse? Metadata { get; init; }
    }

    private record GenerateResponseMetadataResponse
    {
        public string Model { get; init; } = "unknown";
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public int ProcessingTimeMs { get; init; }
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

    private record HandoverSummaryAiRequest
    {
        public string NoticeId { get; init; } = string.Empty;
        public string? NoticeSummary { get; init; }
        public string? NoticeType { get; init; }
        public string? NoticeCategory { get; init; }
        public string? RiskLevel { get; init; }
        public string? ResponseDeadline { get; init; }
        public List<Dictionary<string, string>>? ActivityHistory { get; init; }
        public string? CurrentStatus { get; init; }
        public string? AssignedTo { get; init; }
        public string? AdditionalContext { get; init; }
    }

    private record HandoverSummaryAiResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public HandoverSummaryResponseData? Summary { get; init; }
        public int ProcessingTimeMs { get; init; }
    }

    private record HandoverSummaryResponseData
    {
        public string? Background { get; init; }
        public List<string>? WorkCompleted { get; init; }
        public List<string>? PendingItems { get; init; }
        public List<string>? NextSteps { get; init; }
        public List<Dictionary<string, string>>? KeyDeadlines { get; init; }
        public List<string>? KeyContacts { get; init; }
        public string? RiskSummary { get; init; }
        public DateTime? GeneratedAt { get; init; }
    }

    // Allegation-related request/response DTOs
    private record ExtractAllegationsAiRequest
    {
        public Guid NoticeId { get; init; }
        public string? NoticeText { get; init; }
    }

    private record AllegationExtractionAiResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public List<AllegationAiData>? Allegations { get; init; }
        public decimal? TotalAmount { get; init; }
        public GenerateResponseMetadataResponse? Metadata { get; init; }
    }

    private record AllegationAiData
    {
        public string Id { get; init; } = string.Empty;
        public int SerialNumber { get; init; }
        public string Description { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string? LegalProvision { get; init; }
        public decimal? AmountInvolved { get; init; }
        public string? Period { get; init; }
        public List<string>? EvidenceCited { get; init; }
        public string? PageReference { get; init; }
    }

    private record AllegationWiseDraftAiRequest
    {
        public string? NoticeText { get; init; }
        public Guid NoticeId { get; init; }
        public string Tone { get; init; } = "formal";
        public string Language { get; init; } = "en";
        public List<AllegationAiData>? Allegations { get; init; }
        public Dictionary<string, string>? AllegationOverrides { get; init; }
        public bool IncludeCaseLaw { get; init; } = true;
        public Dictionary<string, object>? TaxpayerContext { get; init; }
    }

    private record AllegationWiseDraftAiResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public List<AllegationAiData>? Allegations { get; init; }
        public List<AllegationResponseAiResponse>? Responses { get; init; }
        public string? CombinedDraft { get; init; }
        public List<string>? MissingInfoSummary { get; init; }
        public GenerateResponseMetadataResponse? Metadata { get; init; }
    }

    private record AllegationResponseAiResponse
    {
        public string AllegationId { get; init; } = string.Empty;
        public string? ResponseText { get; init; }
        public List<string>? LegalBasis { get; init; }
        public List<SupportingDocumentAiResponse>? SupportingEvidence { get; init; }
        public List<string>? MissingInformation { get; init; }
        public List<CaseLawAiResponse>? CaseLawReferences { get; init; }
        public double Confidence { get; init; }
        public string? Status { get; init; }
    }

    private record SupportingDocumentAiResponse
    {
        public string? DocumentName { get; init; }
        public string? DocumentType { get; init; }
        public string? Relevance { get; init; }
        public bool Available { get; init; } = true;
    }

    private record CaseLawAiResponse
    {
        public string? Citation { get; init; }
        public string? Court { get; init; }
        public string? Principle { get; init; }
        public string? Relevance { get; init; }
    }

    private record RegenerateAllegationAiRequest
    {
        public Guid NoticeId { get; init; }
        public required AllegationAiData Allegation { get; init; }
        public string Tone { get; init; } = "formal";
        public string Language { get; init; } = "en";
        public string? CustomInstructions { get; init; }
        public Dictionary<string, object>? TaxpayerContext { get; init; }
    }

    #endregion
}

internal sealed class AiUsageCapException(string message) : InvalidOperationException(message);
