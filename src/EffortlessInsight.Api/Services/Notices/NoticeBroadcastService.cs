using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Service for broadcasting notice status updates to connected clients via SignalR.
/// </summary>
public class NoticeBroadcastService : INoticeBroadcastService
{
    private readonly IHubContext<NoticeHub> _hubContext;
    private readonly ILogger<NoticeBroadcastService> _logger;

    public NoticeBroadcastService(
        IHubContext<NoticeHub> hubContext,
        ILogger<NoticeBroadcastService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task BroadcastStatusUpdateAsync(Notice notice, CancellationToken cancellationToken = default)
    {
        var groupName = $"notices:org:{notice.OrganizationId}";

        var payload = new
        {
            noticeId = notice.Id,
            status = notice.Status,
            processingStatus = notice.ProcessingStatus,
            riskLevel = notice.AiReport?.RiskLevel,
            riskScore = notice.AiReport?.RiskScore,
            noticeType = notice.NoticeType,
            updatedAt = notice.UpdatedAt
        };

        await _hubContext.Clients.Group(groupName)
            .SendAsync("NoticeStatusChanged", payload, cancellationToken);

        _logger.LogDebug("Broadcasted status update for notice {NoticeId} to group {GroupName}. Status: {Status}, ProcessingStatus: {ProcessingStatus}",
            notice.Id, groupName, notice.Status, notice.ProcessingStatus);
    }
}
