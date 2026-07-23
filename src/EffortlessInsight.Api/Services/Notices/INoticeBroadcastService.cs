using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Service for broadcasting notice status updates to connected clients via SignalR.
/// </summary>
public interface INoticeBroadcastService
{
    /// <summary>
    /// Broadcast a notice status update to all clients connected to the organization.
    /// </summary>
    /// <param name="notice">The notice that was updated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BroadcastStatusUpdateAsync(Notice notice, CancellationToken cancellationToken = default);
}
