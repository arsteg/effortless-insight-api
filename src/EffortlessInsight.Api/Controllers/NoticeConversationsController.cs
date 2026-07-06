using System.Security.Claims;
using System.Text.Json;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.AIChat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for managing AI chat conversations on notices.
/// </summary>
[ApiController]
[Route("api/v1/notices/{noticeId:guid}/conversations")]
[Authorize]
public class NoticeConversationsController : ControllerBase
{
    private readonly IConversationService _conversationService;
    private readonly IAIChatService _aiChatService;
    private readonly ILogger<NoticeConversationsController> _logger;

    public NoticeConversationsController(
        IConversationService conversationService,
        IAIChatService aiChatService,
        ILogger<NoticeConversationsController> logger)
    {
        _conversationService = conversationService;
        _aiChatService = aiChatService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid or missing user identifier in authentication token");
        }
        return userId;
    }

    /// <summary>
    /// Get all conversations for a notice.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ConversationListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversations(
        Guid noticeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var result = await _conversationService.GetConversationsAsync(
            noticeId, userId, page, pageSize, cancellationToken);

        return Ok(new ApiResponse<ConversationListDto>(true, result));
    }

    /// <summary>
    /// Create a new conversation for a notice.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ConversationDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateConversation(
        Guid noticeId,
        [FromBody] CreateConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        try
        {
            var result = await _conversationService.CreateConversationAsync(
                noticeId, userId, request, cancellationToken);

            return CreatedAtAction(
                nameof(GetConversation),
                new { noticeId, conversationId = result.Id },
                new ApiResponse<ConversationDetailDto>(true, result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Get a specific conversation with messages.
    /// </summary>
    [HttpGet("{conversationId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ConversationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConversation(
        Guid noticeId,
        Guid conversationId,
        [FromQuery] int messageLimit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var result = await _conversationService.GetConversationAsync(
            conversationId, userId, messageLimit, cursor, cancellationToken);

        if (result == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));

        return Ok(new ApiResponse<ConversationDetailDto>(true, result));
    }

    /// <summary>
    /// Delete a conversation.
    /// </summary>
    [HttpDelete("{conversationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConversation(
        Guid noticeId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var deleted = await _conversationService.DeleteConversationAsync(
            conversationId, userId, cancellationToken);

        if (!deleted)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));

        return NoContent();
    }

    /// <summary>
    /// Archive a conversation.
    /// </summary>
    [HttpPost("{conversationId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveConversation(
        Guid noticeId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var archived = await _conversationService.ArchiveConversationAsync(
            conversationId, userId, cancellationToken);

        if (!archived)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));

        return NoContent();
    }

    /// <summary>
    /// Get suggested questions for a notice.
    /// </summary>
    [HttpGet("~/api/v1/notices/{noticeId:guid}/suggested-questions")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuggestedQuestions(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var questions = await _aiChatService.GetSuggestedQuestionsAsync(noticeId, cancellationToken);
        return Ok(new ApiResponse<List<string>>(true, questions));
    }
}

/// <summary>
/// API endpoints for conversation messages.
/// </summary>
[ApiController]
[Route("api/v1/conversations/{conversationId:guid}")]
[Authorize]
public class ConversationMessagesController : ControllerBase
{
    private readonly IConversationService _conversationService;
    private readonly IAIChatService _aiChatService;
    private readonly ILogger<ConversationMessagesController> _logger;

    public ConversationMessagesController(
        IConversationService conversationService,
        IAIChatService aiChatService,
        ILogger<ConversationMessagesController> logger)
    {
        _conversationService = conversationService;
        _aiChatService = aiChatService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid or missing user identifier in authentication token");
        }
        return userId;
    }

    /// <summary>
    /// Send a message and get AI response (streaming via SSE).
    /// </summary>
    [HttpPost("messages")]
    [Produces("text/event-stream")]
    public async Task SendMessage(
        Guid conversationId,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var userId = GetUserId();

        try
        {
            await foreach (var evt in _aiChatService.StreamMessageAsync(
                conversationId, userId, request, cancellationToken))
            {
                var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (KeyNotFoundException ex)
        {
            var errorEvent = new ChatStreamEvent(ChatEventType.Error, ex.Message);
            var json = JsonSerializer.Serialize(errorEvent);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming message for conversation {ConversationId}", conversationId);
            var errorEvent = new ChatStreamEvent(ChatEventType.Error, "An error occurred processing your message");
            var json = JsonSerializer.Serialize(errorEvent);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        }
    }

    /// <summary>
    /// Send a message and get AI response (non-streaming).
    /// </summary>
    [HttpPost("messages/sync")]
    [ProducesResponseType(typeof(ApiResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessageSync(
        Guid conversationId,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        try
        {
            var result = await _aiChatService.SendMessageAsync(
                conversationId, userId, request, cancellationToken);
            return Ok(new ApiResponse<MessageDto>(true, result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
    }

    /// <summary>
    /// Get messages for a conversation (paginated).
    /// </summary>
    [HttpGet("messages")]
    [ProducesResponseType(typeof(ApiResponse<MessageListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessages(
        Guid conversationId,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var conversation = await _conversationService.GetConversationAsync(
            conversationId, userId, limit, cursor, cancellationToken);

        if (conversation == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Conversation not found"));

        return Ok(new ApiResponse<MessageListDto>(true, new MessageListDto(
            conversation.Messages,
            conversation.HasMore,
            conversation.NextCursor)));
    }

    /// <summary>
    /// Regenerate an assistant message.
    /// </summary>
    [HttpPost("messages/{messageId:guid}/regenerate")]
    [ProducesResponseType(typeof(ApiResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegenerateMessage(
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        try
        {
            var result = await _aiChatService.RegenerateMessageAsync(
                conversationId, messageId, userId, cancellationToken);
            return Ok(new ApiResponse<MessageDto>(true, result));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_OPERATION", ex.Message));
        }
    }

    /// <summary>
    /// Submit feedback for a message.
    /// </summary>
    [HttpPost("messages/{messageId:guid}/feedback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitFeedback(
        Guid conversationId,
        Guid messageId,
        [FromBody] MessageFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();

        try
        {
            await _conversationService.AddFeedbackAsync(
                messageId, userId, request, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
    }
}
