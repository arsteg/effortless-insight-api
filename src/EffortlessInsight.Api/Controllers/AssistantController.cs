using System.Security.Claims;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.AIChat;
using EffortlessInsight.Api.Services.Assistant;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// App-wide end-user assistant gateway. Conversations are persisted here;
/// chat turns are proxied to the AI service with the user's own bearer token
/// forwarded so every tool call inherits the user's permissions.
/// </summary>
[ApiController]
[Route("api/v1/assistant")]
[Authorize]
public class AssistantController : ControllerBase
{
    private const long MaxAudioBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAudioTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg", "audio/mp4", "audio/m4a", "audio/x-m4a",
        "audio/wav", "audio/x-wav", "audio/webm", "audio/ogg", "video/webm",
    };

    private const string FeatureNotAvailableMessage =
        "The AI assistant is not available in your current plan. Upgrade to chat with the assistant.";

    private readonly IAssistantService _assistantService;
    private readonly IAssistantRateLimiter _rateLimiter;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly IFeatureAccessService _featureAccess;
    private readonly AssistantOptions _options;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(
        IAssistantService assistantService,
        IAssistantRateLimiter rateLimiter,
        ICurrentOrganizationService currentOrg,
        IFeatureAccessService featureAccess,
        IOptions<AssistantOptions> options,
        ILogger<AssistantController> logger)
    {
        _assistantService = assistantService;
        _rateLimiter = rateLimiter;
        _currentOrg = currentOrg;
        _featureAccess = featureAccess;
        _options = options.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------ //
    // Conversations
    // ------------------------------------------------------------------ //

    [HttpGet("conversations")]
    [ProducesResponseType(typeof(ApiResponse<AssistantConversationListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetIdentity(out var orgId, out var userId, out var error)) return error!;

        var result = await _assistantService.GetConversationsAsync(orgId, userId, page, pageSize, cancellationToken);
        return Ok(new ApiResponse<AssistantConversationListDto>(true, result));
    }

    [HttpPost("conversations")]
    [ProducesResponseType(typeof(ApiResponse<AssistantConversationDetailDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateConversation(
        [FromBody] CreateAssistantConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AssistantEnabled(out var disabled)) return disabled!;
        if (!TryGetIdentity(out var orgId, out var userId, out var error)) return error!;

        var result = await _assistantService.CreateConversationAsync(orgId, userId, request, cancellationToken);
        return CreatedAtAction(
            nameof(GetConversation),
            new { conversationId = result.Id },
            new ApiResponse<AssistantConversationDetailDto>(true, result));
    }

    [HttpGet("conversations/{conversationId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AssistantConversationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConversation(
        Guid conversationId,
        [FromQuery] int messageLimit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetIdentity(out var orgId, out var userId, out var error)) return error!;

        var result = await _assistantService.GetConversationAsync(
            conversationId, orgId, userId, messageLimit, cancellationToken);
        if (result == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));

        return Ok(new ApiResponse<AssistantConversationDetailDto>(true, result));
    }

    [HttpPatch("conversations/{conversationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameConversation(
        Guid conversationId,
        [FromBody] UpdateAssistantConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetIdentity(out var orgId, out var userId, out var error)) return error!;

        var renamed = await _assistantService.RenameConversationAsync(
            conversationId, orgId, userId, request.Title, cancellationToken);
        return renamed
            ? NoContent()
            : NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));
    }

    [HttpDelete("conversations/{conversationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConversation(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetIdentity(out var orgId, out var userId, out var error)) return error!;

        var deleted = await _assistantService.DeleteConversationAsync(
            conversationId, orgId, userId, cancellationToken);
        return deleted
            ? NoContent()
            : NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));
    }

    // ------------------------------------------------------------------ //
    // Chat turns
    // ------------------------------------------------------------------ //

    /// <summary>Non-streaming chat turn (mobile-friendly).</summary>
    [HttpPost("conversations/{conversationId:guid}/messages/sync")]
    [ProducesResponseType(typeof(ApiResponse<AssistantTurnDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendMessageSync(
        Guid conversationId,
        [FromBody] SendAssistantMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AssistantEnabled(out var disabled)) return disabled!;
        if (!TryGetIdentity(out var orgId, out var userId, out var error)) return error!;

        // AI turns cost real money — paid plans only (free "Notify Only" is excluded).
        if (!await _featureAccess.HasFeatureAccessAsync(orgId, FeatureCodes.AiExplanation, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "FEATURE_NOT_AVAILABLE", FeatureNotAvailableMessage));
        }

        try
        {
            await _rateLimiter.EnforceAsync(userId, cancellationToken);
            var turnContext = BuildTurnContext(conversationId, orgId, userId);
            var result = await _assistantService.SendMessageAsync(turnContext, request, cancellationToken);
            return Ok(new ApiResponse<AssistantTurnDto>(true, result));
        }
        catch (ChatRateLimitExceededException ex)
        {
            Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ApiErrorResponse(false, "RATE_LIMITED", ex.Message));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));
        }
        catch (AssistantUnavailableException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ApiErrorResponse(false, "ASSISTANT_UNAVAILABLE", "The assistant is temporarily unavailable"));
        }
    }

    /// <summary>Streaming chat turn (SSE pass-through from the AI service).</summary>
    [HttpPost("conversations/{conversationId:guid}/messages")]
    [Produces("text/event-stream")]
    public async Task SendMessageStream(
        Guid conversationId,
        [FromBody] SendAssistantMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        // Gates that must reject BEFORE the response becomes an SSE stream
        if (!TryGetIdentity(out var orgId, out var userId, out _))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (!_options.Enabled)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await Response.WriteAsJsonAsync(
                new ApiErrorResponse(false, "ASSISTANT_DISABLED", "The assistant is currently disabled"),
                cancellationToken);
            return;
        }
        if (!await _featureAccess.HasFeatureAccessAsync(orgId, FeatureCodes.AiExplanation, cancellationToken))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsJsonAsync(
                new ApiErrorResponse(false, "FEATURE_NOT_AVAILABLE", FeatureNotAvailableMessage),
                cancellationToken);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        try
        {
            await _rateLimiter.EnforceAsync(userId, cancellationToken);
        }
        catch (ChatRateLimitExceededException ex)
        {
            await WriteEventAsync(
                System.Text.Json.JsonSerializer.Serialize(
                    new { type = "error", message = ex.Message, code = "RATE_LIMITED" }),
                cancellationToken);
            await WriteEventAsync("[DONE]", cancellationToken);
            return;
        }

        var turnContext = BuildTurnContext(conversationId, orgId, userId);
        try
        {
            await foreach (var line in _assistantService
                .StreamMessageAsync(turnContext, request, cancellationToken))
            {
                await Response.WriteAsync(line + "\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (KeyNotFoundException)
        {
            await WriteEventAsync("{\"type\":\"error\",\"message\":\"conversation_not_found\"}", cancellationToken);
            await WriteEventAsync("[DONE]", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // client disconnected — nothing to write
        }
    }

    // ------------------------------------------------------------------ //
    // Voice
    // ------------------------------------------------------------------ //

    /// <summary>Speech-to-text proxy for assistant voice input.</summary>
    [HttpPost("transcribe")]
    [RequestSizeLimit(MaxAudioBytes + 1024)]
    [ProducesResponseType(typeof(ApiResponse<AssistantTranscriptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Transcribe(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (!AssistantEnabled(out var disabled)) return disabled!;
        if (!TryGetIdentity(out var orgId, out var userId, out var error)) return error!;

        if (!await _featureAccess.HasFeatureAccessAsync(orgId, FeatureCodes.AiExplanation, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "FEATURE_NOT_AVAILABLE", FeatureNotAvailableMessage));
        }

        if (file == null || file.Length == 0)
            return BadRequest(new ApiErrorResponse(false, "VALIDATION_ERROR", "Audio file is required"));
        if (file.Length > MaxAudioBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new ApiErrorResponse(false, "FILE_TOO_LARGE", "Audio file is too large (max 5 MB)"));

        var contentType = (file.ContentType ?? "").Split(';')[0].Trim();
        if (!AllowedAudioTypes.Contains(contentType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                new ApiErrorResponse(false, "UNSUPPORTED_MEDIA_TYPE", $"Unsupported audio type: {contentType}"));

        try
        {
            var turnContext = BuildTurnContext(Guid.Empty, orgId, userId);
            await using var stream = file.OpenReadStream();
            var result = await _assistantService.TranscribeAsync(
                turnContext, stream, file.FileName, contentType, file.Length, cancellationToken);
            return Ok(new ApiResponse<AssistantTranscriptionDto>(true, result));
        }
        catch (AssistantUnavailableException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ApiErrorResponse(false, "ASSISTANT_UNAVAILABLE", "Transcription is temporarily unavailable"));
        }
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private bool AssistantEnabled(out IActionResult? result)
    {
        if (_options.Enabled)
        {
            result = null;
            return true;
        }
        result = StatusCode(StatusCodes.Status503ServiceUnavailable,
            new ApiErrorResponse(false, "ASSISTANT_DISABLED", "The assistant is currently disabled"));
        return false;
    }

    private bool TryGetIdentity(out Guid orgId, out Guid userId, out IActionResult? error)
    {
        orgId = _currentOrg.OrganizationId ?? Guid.Empty;
        userId = _currentOrg.UserId ?? Guid.Empty;
        if (orgId == Guid.Empty || userId == Guid.Empty)
        {
            error = Unauthorized(new ApiErrorResponse(false, "UNAUTHORIZED", "Missing organization context"));
            return false;
        }
        error = null;
        return true;
    }

    private AssistantTurnContext BuildTurnContext(Guid conversationId, Guid orgId, Guid userId)
    {
        var platform = Request.Headers.TryGetValue("X-Platform", out var p) && p == "mobile" ? "mobile" : "web";
        // The raw Authorization header ("Bearer xxx") — forwarded to the AI service
        // for on-behalf-of tool calls. Never logged, never persisted.
        var bearer = Request.Headers.Authorization.ToString();
        return new AssistantTurnContext(
            conversationId, orgId, userId,
            _currentOrg.Role ?? "member", platform,
            string.IsNullOrWhiteSpace(bearer) ? null : bearer);
    }

    private async Task WriteEventAsync(string data, CancellationToken ct)
    {
        await Response.WriteAsync($"data: {data}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
