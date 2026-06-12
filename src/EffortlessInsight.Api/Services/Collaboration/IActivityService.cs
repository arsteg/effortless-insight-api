using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Collaboration;

public interface IActivityService
{
    Task LogActivityAsync(
        Guid organizationId,
        Guid? noticeId,
        string activityType,
        Guid? actorId,
        Dictionary<string, object> data,
        string message,
        string actorType = "user");

    Task<ActivityFeedResponseDto> GetActivityFeedForNoticeAsync(
        Guid noticeId,
        Guid userId,
        List<string>? types = null,
        DateTime? since = null,
        int limit = 50);

    Task<ActivityFeedResponseDto> GetActivityFeedForOrganizationAsync(
        Guid organizationId,
        Guid userId,
        List<string>? types = null,
        DateTime? since = null,
        int limit = 50);
}
