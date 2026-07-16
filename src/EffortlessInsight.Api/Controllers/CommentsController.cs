using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Collaboration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1")]
public class CommentsController : ControllerBase
{
    private readonly ICommentService _commentService;
    private readonly ILogger<CommentsController> _logger;

    public CommentsController(
        ICommentService commentService,
        ILogger<CommentsController> logger)
    {
        _commentService = commentService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);

    // ==========================================================================
    // Notice-scoped Comment Endpoints
    // ==========================================================================

    /// <summary>
    /// Get all comments for a notice
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/comments")]
    [ProducesResponseType(typeof(CommentListResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommentsForNotice(
        Guid noticeId,
        [FromQuery] string? visibility = null,
        [FromQuery] bool includeReplies = true,
        [FromQuery] string sortOrder = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var result = await _commentService.GetCommentsForNoticeAsync(
                noticeId, GetUserId(), visibility, includeReplies, sortOrder, page, pageSize);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching comments for notice {NoticeId}", noticeId);
            return StatusCode(500, new { error = "Failed to fetch comments", details = ex.Message });
        }
    }

    /// <summary>
    /// Add a comment to a notice
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/comments")]
    [ProducesResponseType(typeof(CommentResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateComment(Guid noticeId, [FromBody] CreateCommentRequestDto dto)
    {
        try
        {
            var result = await _commentService.CreateCommentAsync(noticeId, dto, GetUserId());
            return CreatedAtAction(nameof(GetCommentById), new { commentId = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating comment for notice {NoticeId}", noticeId);
            return StatusCode(500, new { error = "Failed to create comment", details = ex.Message });
        }
    }

    // ==========================================================================
    // Comment CRUD Endpoints
    // ==========================================================================

    /// <summary>
    /// Get a comment by ID
    /// </summary>
    [HttpGet("comments/{commentId:guid}")]
    [ProducesResponseType(typeof(CommentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCommentById(Guid commentId)
    {
        try
        {
            var comment = await _commentService.GetCommentByIdAsync(commentId, GetUserId());
            return Ok(comment);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new { error = "Comment not found" });
        }
    }

    /// <summary>
    /// Reply to a comment
    /// </summary>
    [HttpPost("comments/{commentId:guid}/replies")]
    [ProducesResponseType(typeof(CommentResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReplyToComment(Guid commentId, [FromBody] CreateCommentRequestDto dto)
    {
        try
        {
            var result = await _commentService.ReplyToCommentAsync(commentId, dto, GetUserId());
            return CreatedAtAction(nameof(GetCommentById), new { commentId = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Edit a comment
    /// </summary>
    [HttpPatch("comments/{commentId:guid}")]
    [ProducesResponseType(typeof(CommentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateComment(Guid commentId, [FromBody] UpdateCommentDto dto)
    {
        try
        {
            var result = await _commentService.UpdateCommentAsync(commentId, dto, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a comment
    /// </summary>
    [HttpDelete("comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteComment(Guid commentId)
    {
        try
        {
            await _commentService.DeleteCommentAsync(commentId, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    // ==========================================================================
    // Reaction Endpoints
    // ==========================================================================

    /// <summary>
    /// Add a reaction to a comment
    /// </summary>
    [HttpPost("comments/{commentId:guid}/reactions")]
    [ProducesResponseType(typeof(ReactionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddReaction(Guid commentId, [FromBody] AddReactionDto dto)
    {
        try
        {
            var result = await _commentService.AddReactionAsync(commentId, dto, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Remove a reaction from a comment
    /// </summary>
    [HttpDelete("comments/{commentId:guid}/reactions/{emoji}")]
    [ProducesResponseType(typeof(ReactionResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveReaction(Guid commentId, string emoji)
    {
        try
        {
            // URL decode the emoji
            var decodedEmoji = Uri.UnescapeDataString(emoji);
            var result = await _commentService.RemoveReactionAsync(commentId, decodedEmoji, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
