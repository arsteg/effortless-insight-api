using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Collaboration;

public interface ICommentService
{
    // Comment CRUD
    Task<CommentResponseDto> CreateCommentAsync(Guid noticeId, CreateCommentRequestDto dto, Guid userId);
    Task<CommentResponseDto> GetCommentByIdAsync(Guid commentId, Guid userId);
    Task<CommentResponseDto> ReplyToCommentAsync(Guid commentId, CreateCommentRequestDto dto, Guid userId);
    Task<CommentResponseDto> UpdateCommentAsync(Guid commentId, UpdateCommentDto dto, Guid userId);
    Task DeleteCommentAsync(Guid commentId, Guid userId);

    // Comment Listing
    Task<CommentListResponseDto> GetCommentsForNoticeAsync(
        Guid noticeId,
        Guid userId,
        string? visibility = null,
        bool includeReplies = true,
        string sortOrder = "desc",
        int page = 1,
        int pageSize = 50);

    // Reactions
    Task<ReactionResponseDto> AddReactionAsync(Guid commentId, AddReactionDto dto, Guid userId);
    Task<ReactionResponseDto> RemoveReactionAsync(Guid commentId, string emoji, Guid userId);

    // Utility
    Task<bool> CanUserEditCommentAsync(Guid commentId, Guid userId);
    Task<bool> CanUserViewCommentAsync(Guid commentId, Guid userId);
}
