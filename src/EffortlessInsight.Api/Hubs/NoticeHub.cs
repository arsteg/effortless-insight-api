using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EffortlessInsight.Api.Hubs;

/// <summary>
/// SignalR hub for real-time notice status updates.
/// Allows clients to subscribe to notice updates for their organization.
/// </summary>
[Authorize]
public class NoticeHub : Hub
{
    private readonly ILogger<NoticeHub> _logger;

    public NoticeHub(ILogger<NoticeHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Join an organization group to receive notice updates.
    /// </summary>
    public async Task JoinOrganization(string organizationId)
    {
        var groupName = $"notices:org:{organizationId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} joined organization group {GroupName}",
            Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Leave an organization group.
    /// </summary>
    public async Task LeaveOrganization(string organizationId)
    {
        var groupName = $"notices:org:{organizationId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} left organization group {GroupName}",
            Context.ConnectionId, groupName);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("NoticeHub: Client {ConnectionId} connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("NoticeHub: Client {ConnectionId} disconnected. Exception: {Exception}",
            Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }
}
