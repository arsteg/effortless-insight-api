using System.Text.RegularExpressions;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Ganss.Xss;
using Markdig;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Collaboration;

public partial class CommentService : ICommentService
{
    private readonly ApplicationDbContext _context;
    private readonly IActivityService _activityService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<CommentService> _logger;
    private readonly HtmlSanitizer _htmlSanitizer;
    private readonly MarkdownPipeline _markdownPipeline;

    private const int MaxEditHours = 24;
    private const int MaxEditHistory = 10;
    private const int MaxReplyDepth = 3;

    public CommentService(
        ApplicationDbContext context,
        IActivityService activityService,
        INotificationService notificationService,
        ILogger<CommentService> logger)
    {
        _context = context;
        _activityService = activityService;
        _notificationService = notificationService;
        _logger = logger;

        // Configure HTML sanitizer with whitelist
        _htmlSanitizer = new HtmlSanitizer();
        _htmlSanitizer.AllowedTags.Clear();
        _htmlSanitizer.AllowedTags.Add("p");
        _htmlSanitizer.AllowedTags.Add("strong");
        _htmlSanitizer.AllowedTags.Add("em");
        _htmlSanitizer.AllowedTags.Add("a");
        _htmlSanitizer.AllowedTags.Add("code");
        _htmlSanitizer.AllowedTags.Add("pre");
        _htmlSanitizer.AllowedTags.Add("ul");
        _htmlSanitizer.AllowedTags.Add("ol");
        _htmlSanitizer.AllowedTags.Add("li");
        _htmlSanitizer.AllowedTags.Add("br");
        _htmlSanitizer.AllowedTags.Add("span");
        _htmlSanitizer.AllowedAttributes.Add("href");
        _htmlSanitizer.AllowedAttributes.Add("class");
        _htmlSanitizer.AllowedAttributes.Add("data-user-id");

        // Configure Markdig for markdown processing
        _markdownPipeline = new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .Build();
    }

    public async Task<CommentResponseDto> CreateCommentAsync(Guid noticeId, CreateCommentRequestDto dto, Guid userId)
    {
        var notice = await _context.Notices
            .FirstOrDefaultAsync(n => n.Id == noticeId)
            ?? throw new KeyNotFoundException("Notice not found");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found");

        // Process content
        var (contentHtml, mentions) = await ProcessContentAsync(dto.Content, notice.OrganizationId);

        var comment = new Comment
        {
            NoticeId = noticeId,
            UserId = userId,
            Content = dto.Content,
            ContentHtml = contentHtml,
            Visibility = dto.Visibility ?? CommentVisibility.All,
            Mentions = mentions.Select(m => m.UserId).ToList(),
            AttachmentUrls = dto.AttachmentUrls,
            IsInternal = dto.Visibility == CommentVisibility.Internal,
            Depth = 0
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            notice.OrganizationId,
            noticeId,
            ActivityTypes.CommentAdded,
            userId,
            new Dictionary<string, object>
            {
                ["commentId"] = comment.Id,
                ["preview"] = TruncateContent(dto.Content, 100),
                ["mentions"] = mentions.Select(m => m.Name).ToList(),
                ["visibility"] = comment.Visibility
            },
            "commented"
        );

        // Log mention activities and send notifications
        var mentionedUserIds = mentions.Where(m => m.UserId != userId).Select(m => m.UserId).ToList();
        foreach (var mention in mentions.Where(m => m.UserId != userId))
        {
            await _activityService.LogActivityAsync(
                notice.OrganizationId,
                noticeId,
                ActivityTypes.UserMentioned,
                userId,
                new Dictionary<string, object>
                {
                    ["commentId"] = comment.Id,
                    ["mentionedUserId"] = mention.UserId,
                    ["mentionedUserName"] = mention.Name
                },
                $"mentioned @{mention.Username}"
            );
        }

        // Send mention notifications (fire and forget)
        if (mentionedUserIds.Any())
        {
            _ = _notificationService.NotifyMentionAsync(comment, mentionedUserIds);
        }

        return await GetCommentResponseAsync(comment.Id, userId);
    }

    public async Task<CommentResponseDto> ReplyToCommentAsync(Guid commentId, CreateCommentRequestDto dto, Guid userId)
    {
        var parentComment = await _context.Comments
            .Include(c => c.Notice)
            .FirstOrDefaultAsync(c => c.Id == commentId)
            ?? throw new KeyNotFoundException("Comment not found");

        // Check max reply depth
        if (parentComment.Depth >= MaxReplyDepth)
        {
            throw new InvalidOperationException($"Maximum reply depth of {MaxReplyDepth} exceeded");
        }

        var (contentHtml, mentions) = await ProcessContentAsync(dto.Content, parentComment.Notice.OrganizationId);

        var reply = new Comment
        {
            NoticeId = parentComment.NoticeId,
            ParentId = commentId,
            UserId = userId,
            Content = dto.Content,
            ContentHtml = contentHtml,
            Visibility = parentComment.Visibility, // Inherit visibility from parent
            Mentions = mentions.Select(m => m.UserId).ToList(),
            AttachmentUrls = dto.AttachmentUrls,
            IsInternal = parentComment.IsInternal,
            Depth = parentComment.Depth + 1
        };

        _context.Comments.Add(reply);
        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            parentComment.Notice.OrganizationId,
            parentComment.NoticeId,
            ActivityTypes.CommentAdded,
            userId,
            new Dictionary<string, object>
            {
                ["commentId"] = reply.Id,
                ["parentCommentId"] = commentId,
                ["preview"] = TruncateContent(dto.Content, 100),
                ["isReply"] = true
            },
            "replied to a comment"
        );

        // Send reply notification to parent comment author (fire and forget)
        if (parentComment.UserId != userId)
        {
            _ = _notificationService.NotifyCommentReplyAsync(reply, parentComment);
        }

        // Send mention notifications for any mentioned users
        var mentionedUserIds = mentions.Where(m => m.UserId != userId).Select(m => m.UserId).ToList();
        if (mentionedUserIds.Any())
        {
            _ = _notificationService.NotifyMentionAsync(reply, mentionedUserIds);
        }

        return await GetCommentResponseAsync(reply.Id, userId);
    }

    public async Task<CommentResponseDto> UpdateCommentAsync(Guid commentId, UpdateCommentDto dto, Guid userId)
    {
        var comment = await _context.Comments
            .Include(c => c.Notice)
            .FirstOrDefaultAsync(c => c.Id == commentId)
            ?? throw new KeyNotFoundException("Comment not found");

        if (comment.UserId != userId)
        {
            throw new UnauthorizedAccessException("You can only edit your own comments");
        }

        // Check edit time limit (24 hours)
        var hoursSinceCreation = (DateTime.UtcNow - comment.CreatedAt).TotalHours;
        if (hoursSinceCreation > MaxEditHours)
        {
            throw new InvalidOperationException($"Comments can only be edited within {MaxEditHours} hours");
        }

        // Check edit count limit
        if (comment.EditCount >= MaxEditHistory)
        {
            throw new InvalidOperationException($"Maximum of {MaxEditHistory} edits allowed");
        }

        // Save edit history
        _context.CommentEditHistory.Add(new CommentEditHistory
        {
            CommentId = commentId,
            PreviousContent = comment.Content,
            EditedAt = DateTime.UtcNow
        });

        // Process new content
        var (contentHtml, mentions) = await ProcessContentAsync(dto.Content, comment.Notice.OrganizationId);

        comment.Content = dto.Content;
        comment.ContentHtml = contentHtml;
        comment.Mentions = mentions.Select(m => m.UserId).ToList();
        comment.IsEdited = true;
        comment.EditCount++;
        comment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            comment.Notice.OrganizationId,
            comment.NoticeId,
            ActivityTypes.CommentEdited,
            userId,
            new Dictionary<string, object>
            {
                ["commentId"] = comment.Id,
                ["editCount"] = comment.EditCount
            },
            "edited a comment"
        );

        return await GetCommentResponseAsync(comment.Id, userId);
    }

    public async Task DeleteCommentAsync(Guid commentId, Guid userId)
    {
        var comment = await _context.Comments
            .Include(c => c.Notice)
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == commentId)
            ?? throw new KeyNotFoundException("Comment not found");

        if (comment.UserId != userId)
        {
            // Check if user is admin/manager
            var member = await _context.OrganizationMembers
                .FirstOrDefaultAsync(m => m.OrganizationId == comment.Notice.OrganizationId && m.UserId == userId);

            if (member?.Role != "admin" && member?.Role != "manager")
            {
                throw new UnauthorizedAccessException("You can only delete your own comments");
            }
        }

        // Soft delete - keep placeholder if there are replies
        if (comment.Replies.Any(r => r.DeletedAt == null))
        {
            comment.IsDeleted = true;
            comment.Content = "[deleted]";
            comment.ContentHtml = "<p>[This comment was deleted]</p>";
            comment.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            comment.DeletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            comment.Notice.OrganizationId,
            comment.NoticeId,
            ActivityTypes.CommentDeleted,
            userId,
            new Dictionary<string, object> { ["commentId"] = comment.Id },
            "deleted a comment"
        );
    }

    public async Task<CommentListResponseDto> GetCommentsForNoticeAsync(
        Guid noticeId,
        Guid userId,
        string? visibility = null,
        bool includeReplies = true,
        string sortOrder = "desc",
        int page = 1,
        int pageSize = 50)
    {
        var notice = await _context.Notices
            .FirstOrDefaultAsync(n => n.Id == noticeId)
            ?? throw new KeyNotFoundException("Notice not found");

        // Check if user can view internal comments
        var canViewInternal = await CanViewInternalComments(notice.OrganizationId, userId);

        var query = _context.Comments
            .Include(c => c.User)
            .Include(c => c.Reactions).ThenInclude(r => r.User)
            .Where(c => c.NoticeId == noticeId && c.ParentId == null); // Only top-level comments

        if (!canViewInternal)
        {
            query = query.Where(c => c.Visibility != CommentVisibility.Internal);
        }
        else if (!string.IsNullOrEmpty(visibility))
        {
            query = query.Where(c => c.Visibility == visibility);
        }

        var totalItems = await query.CountAsync();

        query = sortOrder.ToLower() == "asc"
            ? query.OrderBy(c => c.CreatedAt)
            : query.OrderByDescending(c => c.CreatedAt);

        var comments = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Load replies if requested
        if (includeReplies && comments.Any())
        {
            var commentIds = comments.Select(c => c.Id).ToList();
            var replies = await _context.Comments
                .Include(c => c.User)
                .Include(c => c.Reactions).ThenInclude(r => r.User)
                .Where(c => c.NoticeId == noticeId && c.ParentId != null)
                .ToListAsync();

            // Build hierarchy
            foreach (var comment in comments)
            {
                LoadRepliesRecursive(comment, replies);
            }
        }

        // Pre-fetch mention user info for all comments and replies
        var allMentionIds = new HashSet<Guid>();
        foreach (var comment in comments)
        {
            var ids = GetAllMentionIds(comment);
            foreach (var id in ids)
                allMentionIds.Add(id);
        }
        var mentionLookup = await GetMentionLookupAsync(allMentionIds);

        var commentDtos = comments.Select(c => MapToResponseDto(c, userId, canViewInternal, mentionLookup)).ToList();

        return new CommentListResponseDto(
            Comments: commentDtos,
            Pagination: new PaginationDto(
                Page: page,
                PageSize: pageSize,
                TotalItems: totalItems,
                TotalPages: (int)Math.Ceiling(totalItems / (double)pageSize)
            )
        );
    }

    public async Task<ReactionResponseDto> AddReactionAsync(Guid commentId, AddReactionDto dto, Guid userId)
    {
        if (!AllowedReactions.IsValid(dto.Emoji))
        {
            throw new InvalidOperationException($"Invalid emoji. Allowed: {string.Join(" ", AllowedReactions.All)}");
        }

        var comment = await _context.Comments
            .Include(c => c.Notice)
            .FirstOrDefaultAsync(c => c.Id == commentId)
            ?? throw new KeyNotFoundException("Comment not found");

        // Check if reaction already exists
        var existingReaction = await _context.CommentReactions
            .FirstOrDefaultAsync(r => r.CommentId == commentId && r.UserId == userId && r.Emoji == dto.Emoji);

        if (existingReaction != null)
        {
            return await GetReactionResponseAsync(commentId, userId);
        }

        _context.CommentReactions.Add(new CommentReaction
        {
            CommentId = commentId,
            UserId = userId,
            Emoji = dto.Emoji,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        // Log activity (only for non-self reactions)
        if (comment.UserId != userId)
        {
            await _activityService.LogActivityAsync(
                comment.Notice.OrganizationId,
                comment.NoticeId,
                ActivityTypes.CommentReaction,
                userId,
                new Dictionary<string, object>
                {
                    ["commentId"] = commentId,
                    ["emoji"] = dto.Emoji
                },
                $"reacted with {dto.Emoji}"
            );
        }

        return await GetReactionResponseAsync(commentId, userId);
    }

    public async Task<ReactionResponseDto> RemoveReactionAsync(Guid commentId, string emoji, Guid userId)
    {
        var reaction = await _context.CommentReactions
            .FirstOrDefaultAsync(r => r.CommentId == commentId && r.UserId == userId && r.Emoji == emoji);

        if (reaction != null)
        {
            _context.CommentReactions.Remove(reaction);
            await _context.SaveChangesAsync();
        }

        return await GetReactionResponseAsync(commentId, userId);
    }

    public async Task<bool> CanUserEditCommentAsync(Guid commentId, Guid userId)
    {
        var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (comment == null) return false;

        if (comment.UserId != userId) return false;

        var hoursSinceCreation = (DateTime.UtcNow - comment.CreatedAt).TotalHours;
        return hoursSinceCreation <= MaxEditHours && comment.EditCount < MaxEditHistory;
    }

    public async Task<bool> CanUserViewCommentAsync(Guid commentId, Guid userId)
    {
        var comment = await _context.Comments
            .Include(c => c.Notice)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment == null) return false;

        if (comment.Visibility != CommentVisibility.Internal)
            return true;

        return await CanViewInternalComments(comment.Notice.OrganizationId, userId);
    }

    // Private helpers

    private async Task<(string contentHtml, List<MentionDto> mentions)> ProcessContentAsync(string content, Guid organizationId)
    {
        var mentions = new List<MentionDto>();

        // Extract @mentions using regex
        var mentionPattern = MentionRegex();
        var processedContent = content;

        var matches = mentionPattern.Matches(content);
        foreach (Match match in matches)
        {
            var username = match.Groups[1].Value;

            // Look up user by username
            var user = await _context.Users
                .Where(u => u.UserName != null && u.UserName.ToLower() == username.ToLower())
                .FirstOrDefaultAsync();

            if (user != null)
            {
                // Verify user is org member
                var isMember = await _context.OrganizationMembers
                    .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id);

                if (isMember)
                {
                    mentions.Add(new MentionDto(
                        UserId: user.Id,
                        Username: user.UserName ?? username,
                        Name: user.Name
                    ));

                    // Replace @username with mention span
                    processedContent = processedContent.Replace(
                        $"@{username}",
                        $"<span class=\"mention\" data-user-id=\"{user.Id}\">@{user.Name}</span>"
                    );
                }
            }
        }

        // Convert markdown to HTML
        var html = Markdown.ToHtml(processedContent, _markdownPipeline);

        // Sanitize HTML to prevent XSS
        var sanitizedHtml = _htmlSanitizer.Sanitize(html);

        return (sanitizedHtml, mentions);
    }

    private async Task<bool> CanViewInternalComments(Guid organizationId, Guid userId)
    {
        var member = await _context.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId);

        // Clients cannot view internal comments
        return member?.Role != "client";
    }

    private static void LoadRepliesRecursive(Comment parent, List<Comment> allReplies)
    {
        var directReplies = allReplies.Where(r => r.ParentId == parent.Id).ToList();
        parent.Replies = directReplies;

        foreach (var reply in directReplies)
        {
            LoadRepliesRecursive(reply, allReplies);
        }
    }

    public async Task<CommentResponseDto> GetCommentByIdAsync(Guid commentId, Guid userId)
    {
        var canView = await CanUserViewCommentAsync(commentId, userId);
        if (!canView)
        {
            throw new UnauthorizedAccessException("You do not have permission to view this comment");
        }

        return await GetCommentResponseAsync(commentId, userId);
    }

    private async Task<CommentResponseDto> GetCommentResponseAsync(Guid commentId, Guid userId)
    {
        var comment = await _context.Comments
            .Include(c => c.User)
            .Include(c => c.Reactions).ThenInclude(r => r.User)
            .Include(c => c.Replies).ThenInclude(r => r.User)
            .Include(c => c.Replies).ThenInclude(r => r.Reactions).ThenInclude(r => r.User)
            .Include(c => c.Notice)
            .FirstOrDefaultAsync(c => c.Id == commentId)
            ?? throw new KeyNotFoundException("Comment not found");

        var canViewInternal = await CanViewInternalComments(comment.Notice.OrganizationId, userId);

        // Pre-fetch mention user info for this comment and its replies
        var allMentionIds = GetAllMentionIds(comment);
        var mentionLookup = await GetMentionLookupAsync(allMentionIds);

        return MapToResponseDto(comment, userId, canViewInternal, mentionLookup);
    }

    private static HashSet<Guid> GetAllMentionIds(Comment comment)
    {
        var ids = new HashSet<Guid>();
        if (comment.Mentions != null)
        {
            foreach (var id in comment.Mentions)
                ids.Add(id);
        }
        if (comment.Replies != null)
        {
            foreach (var reply in comment.Replies)
            {
                var replyIds = GetAllMentionIds(reply);
                foreach (var id in replyIds)
                    ids.Add(id);
            }
        }
        return ids;
    }

    private async Task<Dictionary<Guid, (string Username, string Name)>> GetMentionLookupAsync(IEnumerable<Guid> mentionIds)
    {
        var idList = mentionIds.ToList();
        if (!idList.Any())
            return new Dictionary<Guid, (string Username, string Name)>();

        var users = await _context.Users
            .Where(u => idList.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.Name })
            .ToListAsync();

        return users.ToDictionary(
            u => u.Id,
            u => (u.UserName ?? "", u.Name));
    }

    private async Task<ReactionResponseDto> GetReactionResponseAsync(Guid commentId, Guid userId)
    {
        var reactions = await _context.CommentReactions
            .Include(r => r.User)
            .Where(r => r.CommentId == commentId)
            .ToListAsync();

        var reactionSummaries = reactions
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionSummaryDto(
                Emoji: g.Key,
                Count: g.Count(),
                Users: g.Select(r => r.User?.Name ?? "Unknown").Take(5).ToList(),
                HasReacted: g.Any(r => r.UserId == userId)
            ))
            .ToList();

        return new ReactionResponseDto(commentId, reactionSummaries);
    }

    private static CommentResponseDto MapToResponseDto(
        Comment comment,
        Guid currentUserId,
        bool canViewInternal,
        Dictionary<Guid, (string Username, string Name)>? mentionLookup = null)
    {
        var reactions = comment.Reactions
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionSummaryDto(
                Emoji: g.Key,
                Count: g.Count(),
                Users: g.Select(r => r.User?.Name ?? "Unknown").Take(5).ToList(),
                HasReacted: g.Any(r => r.UserId == currentUserId)
            ))
            .ToList();

        var mentions = comment.Mentions?.Select(m =>
        {
            if (mentionLookup != null && mentionLookup.TryGetValue(m, out var userInfo))
            {
                return new MentionDto(m, userInfo.Username, userInfo.Name);
            }
            return new MentionDto(m, "", "");
        }).ToList();

        List<CommentResponseDto>? replies = null;
        if (comment.Replies?.Any() == true)
        {
            replies = comment.Replies
                .Where(r => r.DeletedAt == null)
                .Where(r => canViewInternal || r.Visibility != CommentVisibility.Internal)
                .OrderBy(r => r.CreatedAt)
                .Select(r => MapToResponseDto(r, currentUserId, canViewInternal, mentionLookup))
                .ToList();
        }

        return new CommentResponseDto(
            Id: comment.Id,
            NoticeId: comment.NoticeId,
            Content: comment.Content,
            ContentHtml: comment.ContentHtml,
            Visibility: comment.Visibility,
            Mentions: mentions,
            AttachmentUrls: comment.AttachmentUrls,
            Reactions: reactions,
            ReplyCount: comment.Replies?.Count(r => r.DeletedAt == null) ?? 0,
            Author: new CommentAuthorDto(
                Id: comment.UserId,
                Name: comment.User?.Name ?? "Unknown",
                AvatarUrl: comment.User?.AvatarUrl
            ),
            CreatedAt: comment.CreatedAt,
            UpdatedAt: comment.UpdatedAt ?? comment.CreatedAt,
            IsEdited: comment.IsEdited,
            IsDeleted: comment.IsDeleted,
            ParentCommentId: comment.ParentId,
            Replies: replies
        );
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return content[..(maxLength - 3)] + "...";
    }

    [GeneratedRegex(@"@(\w+(?:\.\w+)*)")]
    private static partial Regex MentionRegex();
}
