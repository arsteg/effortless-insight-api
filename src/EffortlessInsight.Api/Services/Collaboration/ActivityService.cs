using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Collaboration;

public class ActivityService : IActivityService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(ApplicationDbContext context, ILogger<ActivityService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogActivityAsync(
        Guid organizationId,
        Guid? noticeId,
        string activityType,
        Guid? actorId,
        Dictionary<string, object> data,
        string message,
        string actorType = "user")
    {
        var activity = new ActivityLog
        {
            OrganizationId = organizationId,
            NoticeId = noticeId,
            ActivityType = activityType,
            ActorId = actorId,
            ActorType = actorType,
            Data = data,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };

        _context.ActivityLogs.Add(activity);

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogDebug(
                "Activity logged: {ActivityType} by {ActorId} on notice {NoticeId}",
                activityType, actorId, noticeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log activity {ActivityType}", activityType);
            // Don't throw - activity logging should not break main operations
        }
    }

    public async Task<ActivityFeedResponseDto> GetActivityFeedForNoticeAsync(
        Guid noticeId,
        Guid userId,
        List<string>? types = null,
        DateTime? since = null,
        int limit = 50)
    {
        var query = _context.ActivityLogs
            .Include(a => a.Actor)
            .Where(a => a.NoticeId == noticeId);

        if (types?.Any() == true)
        {
            query = query.Where(a => types.Contains(a.ActivityType));
        }

        if (since.HasValue)
        {
            query = query.Where(a => a.CreatedAt > since.Value);
        }

        // Limit to prevent memory issues
        limit = Math.Min(limit, 100);

        var activities = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit + 1)
            .ToListAsync();

        var hasMore = activities.Count > limit;
        if (hasMore)
        {
            activities = activities.Take(limit).ToList();
        }

        var activityDtos = activities.Select(MapToDto).ToList();
        var nextCursor = hasMore && activities.Any()
            ? activities.Last().CreatedAt.ToString("o")
            : null;

        return new ActivityFeedResponseDto(activityDtos, hasMore, nextCursor);
    }

    public async Task<ActivityFeedResponseDto> GetActivityFeedForOrganizationAsync(
        Guid organizationId,
        Guid userId,
        List<string>? types = null,
        DateTime? since = null,
        int limit = 50)
    {
        var query = _context.ActivityLogs
            .Include(a => a.Actor)
            .Where(a => a.OrganizationId == organizationId);

        if (types?.Any() == true)
        {
            query = query.Where(a => types.Contains(a.ActivityType));
        }

        if (since.HasValue)
        {
            query = query.Where(a => a.CreatedAt > since.Value);
        }

        limit = Math.Min(limit, 100);

        var activities = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit + 1)
            .ToListAsync();

        var hasMore = activities.Count > limit;
        if (hasMore)
        {
            activities = activities.Take(limit).ToList();
        }

        var activityDtos = activities.Select(MapToDto).ToList();
        var nextCursor = hasMore && activities.Any()
            ? activities.Last().CreatedAt.ToString("o")
            : null;

        return new ActivityFeedResponseDto(activityDtos, hasMore, nextCursor);
    }

    private static ActivityDto MapToDto(ActivityLog activity)
    {
        return new ActivityDto(
            Id: activity.Id,
            Type: activity.ActivityType,
            Timestamp: activity.CreatedAt,
            Actor: activity.Actor != null ? new ActivityActorDto(
                Id: activity.Actor.Id,
                Name: activity.Actor.Name,
                AvatarUrl: activity.Actor.AvatarUrl
            ) : null,
            Data: activity.Data,
            Message: activity.Message
        );
    }
}
