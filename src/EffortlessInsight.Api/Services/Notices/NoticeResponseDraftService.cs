using System.Diagnostics;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.AIChat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Implementation of AI-powered auto-drafting for notice responses.
/// Uses the dedicated AI service for all AI operations.
/// </summary>
public class NoticeResponseDraftService : INoticeResponseDraftService
{
    private readonly ApplicationDbContext _db;
    private readonly IAiServiceClient _aiServiceClient;
    private readonly IDistributedCache _cache;
    private readonly ILogger<NoticeResponseDraftService> _logger;

    // Rate limiting: max 10 requests per organization per minute
    private const int RateLimitMaxRequests = 10;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    public NoticeResponseDraftService(
        ApplicationDbContext db,
        IAiServiceClient aiServiceClient,
        IDistributedCache cache,
        ILogger<NoticeResponseDraftService> logger)
    {
        _db = db;
        _aiServiceClient = aiServiceClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<AutoDraftResult> GenerateAutoDraftAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        AutoDraftOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Sanitize and validate options
        options = (options ?? new AutoDraftOptions()).Sanitize();
        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            return AutoDraftResult.Failure("VALIDATION_ERROR", string.Join("; ", validationErrors));
        }

        try
        {
            // Rate limiting check
            var rateLimitResult = await CheckRateLimitAsync(organizationId, cancellationToken);
            if (!rateLimitResult.Allowed)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for organization {OrganizationId}. Current count: {Count}",
                    organizationId, rateLimitResult.CurrentCount);
                return AutoDraftResult.Failure("RATE_LIMIT_EXCEEDED",
                    $"Too many requests. Please wait {rateLimitResult.RetryAfterSeconds} seconds before trying again.");
            }

            // Verify notice exists and belongs to organization
            var noticeExists = await _db.Notices
                .AsNoTracking()
                .AnyAsync(n => n.Id == noticeId && n.OrganizationId == organizationId, cancellationToken);

            if (!noticeExists)
            {
                _logger.LogWarning(
                    "Notice not found or access denied. NoticeId: {NoticeId}, OrganizationId: {OrganizationId}",
                    noticeId, organizationId);
                return AutoDraftResult.Failure("NOTICE_NOT_FOUND", "Notice not found or access denied");
            }

            _logger.LogInformation(
                "Generating auto-draft for notice {NoticeId}. Tone: {Tone}, Language: {Language}",
                noticeId, options.Tone, options.Language);

            // Call AI service
            var aiOptions = new GenerateResponseOptions
            {
                Tone = options.Tone,
                Language = options.Language,
                PointsToAddress = options.PointsToAddress,
                AdditionalInstructions = options.AdditionalInstructions
            };

            var result = await _aiServiceClient.GenerateResponseDraftAsync(noticeId, aiOptions);
            stopwatch.Stop();

            if (!result.Success)
            {
                _logger.LogError(
                    "AI service failed for notice {NoticeId}: {Error}",
                    noticeId, result.Error);

                // Map specific errors
                if (result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return AutoDraftResult.Failure("NO_CONTENT",
                        "Notice has no extracted content or AI analysis. Please wait for processing to complete.");
                }

                return AutoDraftResult.Failure("AI_ERROR", result.Error ?? "Failed to generate draft");
            }

            if (string.IsNullOrWhiteSpace(result.Draft))
            {
                _logger.LogWarning("AI service returned empty content for notice {NoticeId}", noticeId);
                return AutoDraftResult.Failure("EMPTY_RESPONSE", "The AI failed to generate a valid response. Please try again.");
            }

            // Log audit (fire and forget)
            _ = LogAuditAsync(noticeId, organizationId, userId, result, (int)stopwatch.ElapsedMilliseconds, CancellationToken.None);

            _logger.LogInformation(
                "Auto-draft generated successfully for notice {NoticeId}. " +
                "Tokens: {InputTokens}/{OutputTokens}, Time: {TimeMs}ms",
                noticeId,
                result.Metadata?.InputTokens ?? 0,
                result.Metadata?.OutputTokens ?? 0,
                stopwatch.ElapsedMilliseconds);

            // Get notice type for metadata
            var noticeType = await _db.Notices
                .AsNoTracking()
                .Where(n => n.Id == noticeId)
                .Select(n => n.NoticeType)
                .FirstOrDefaultAsync(cancellationToken);

            return AutoDraftResult.Ok(result.Draft, new AutoDraftMetadata
            {
                Model = result.Metadata?.Model ?? "unknown",
                ProcessingTimeMs = result.Metadata?.ProcessingTimeMs ?? (int)stopwatch.ElapsedMilliseconds,
                NoticeType = noticeType,
                InputTokens = result.Metadata?.InputTokens ?? 0,
                OutputTokens = result.Metadata?.OutputTokens ?? 0,
                EstimatedCost = EstimateCost(
                    result.Metadata?.Model ?? "gpt-4o",
                    result.Metadata?.InputTokens ?? 0,
                    result.Metadata?.OutputTokens ?? 0)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Auto-draft request cancelled for notice {NoticeId}", noticeId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error generating auto-draft for notice {NoticeId}, Organization: {OrganizationId}",
                noticeId, organizationId);
            return AutoDraftResult.Failure("INTERNAL_ERROR", "An unexpected error occurred while generating the draft");
        }
    }

    private async Task<(bool Allowed, int CurrentCount, int RetryAfterSeconds)> CheckRateLimitAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"auto_draft_rate:{organizationId}";

        try
        {
            var countStr = await _cache.GetStringAsync(cacheKey, cancellationToken);
            var currentCount = int.TryParse(countStr, out var c) ? c : 0;

            if (currentCount >= RateLimitMaxRequests)
            {
                return (false, currentCount, (int)RateLimitWindow.TotalSeconds);
            }

            // Increment counter
            await _cache.SetStringAsync(
                cacheKey,
                (currentCount + 1).ToString(),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = RateLimitWindow
                },
                cancellationToken);

            return (true, currentCount + 1, 0);
        }
        catch (Exception ex)
        {
            // If cache fails, allow the request but log the error
            _logger.LogWarning(ex, "Failed to check rate limit for organization {OrganizationId}", organizationId);
            return (true, 0, 0);
        }
    }

    /// <summary>
    /// Estimates the cost of an AI request based on the model and token usage.
    /// Prices are approximate and should be updated periodically.
    /// </summary>
    private static decimal EstimateCost(string model, int inputTokens, int outputTokens)
    {
        // Pricing per 1M tokens (as of 2024)
        var (inputCostPer1M, outputCostPer1M) = model.ToLowerInvariant() switch
        {
            var m when m.Contains("gpt-4o") => (2.50m, 10.00m),
            var m when m.Contains("gpt-4-turbo") => (10.00m, 30.00m),
            var m when m.Contains("gpt-4") => (30.00m, 60.00m),
            var m when m.Contains("gpt-3.5") => (0.50m, 1.50m),
            var m when m.Contains("claude-3-opus") => (15.00m, 75.00m),
            var m when m.Contains("claude-3-sonnet") => (3.00m, 15.00m),
            var m when m.Contains("claude-3-haiku") => (0.25m, 1.25m),
            _ => (5.00m, 15.00m) // Default to GPT-4o equivalent
        };

        return (inputTokens * inputCostPer1M / 1_000_000m) + (outputTokens * outputCostPer1M / 1_000_000m);
    }

    private async Task LogAuditAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        GenerateResponseResult result,
        int processingTimeMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var notice = await _db.Notices
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);

            var audit = new AIAuditLog
            {
                OrganizationId = organizationId,
                UserId = userId,
                ModelId = result.Metadata?.Model ?? "unknown",
                PromptVersion = $"auto-draft-v1:{notice?.NoticeType ?? "unknown"}",
                InputTokens = result.Metadata?.InputTokens ?? 0,
                OutputTokens = result.Metadata?.OutputTokens ?? 0,
                TotalTokens = (result.Metadata?.InputTokens ?? 0) + (result.Metadata?.OutputTokens ?? 0),
                EstimatedCost = EstimateCost(
                    result.Metadata?.Model ?? "gpt-4o",
                    result.Metadata?.InputTokens ?? 0,
                    result.Metadata?.OutputTokens ?? 0),
                ResponseTimeMs = processingTimeMs,
                Status = result.Success ? AIAuditStatus.Success : AIAuditStatus.Error,
                ErrorCode = result.Success ? null : "AI_ERROR",
                ErrorMessage = result.Success ? null : result.Error
            };

            _db.AIAuditLogs.Add(audit);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log auto-draft audit for notice {NoticeId}", noticeId);
        }
    }
}
