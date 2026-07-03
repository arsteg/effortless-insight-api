using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.AIChat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EffortlessInsight.Api.Hubs;

/// <summary>
/// SignalR hub for real-time AI chat streaming.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly IAIChatService _aiChatService;
    private readonly IConversationService _conversationService;
    private readonly ILogger<ChatHub> _logger;

    // Track active streaming operations for cancellation support
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _activeStreams = new();

    public ChatHub(
        IAIChatService aiChatService,
        IConversationService conversationService,
        ILogger<ChatHub> logger)
    {
        _aiChatService = aiChatService;
        _conversationService = conversationService;
        _logger = logger;
    }

    /// <summary>
    /// Join a conversation room to receive updates.
    /// </summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var groupName = GetConversationGroup(conversationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug(
            "Client {ConnectionId} joined conversation {ConversationId}",
            Context.ConnectionId, conversationId);
    }

    /// <summary>
    /// Leave a conversation room.
    /// </summary>
    public async Task LeaveConversation(Guid conversationId)
    {
        var groupName = GetConversationGroup(conversationId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug(
            "Client {ConnectionId} left conversation {ConversationId}",
            Context.ConnectionId, conversationId);
    }

    /// <summary>
    /// Send a message and stream the AI response.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> SendMessage(
        Guid conversationId,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!userId.HasValue)
        {
            yield return new ChatStreamEvent(ChatEventType.Error, "User not authenticated");
            yield break;
        }

        // Create a linked cancellation token for stop generation support
        var streamKey = GetStreamKey(conversationId);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeStreams[streamKey] = cts;

        try
        {
            var request = new SendMessageRequest(message, Stream: true);

            await foreach (var evt in _aiChatService.StreamMessageAsync(
                conversationId, userId.Value, request, cts.Token))
            {
                yield return evt;

                // Also broadcast to other clients in the conversation
                if (evt.Type == ChatEventType.UserMessageSaved ||
                    evt.Type == ChatEventType.StreamCompleted)
                {
                    await BroadcastToOthersAsync(conversationId, evt);
                }
            }
        }
        finally
        {
            _activeStreams.TryRemove(streamKey, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Stop the current generation for a conversation.
    /// </summary>
    public Task StopGeneration(Guid conversationId)
    {
        var streamKey = GetStreamKey(conversationId);

        if (_activeStreams.TryGetValue(streamKey, out var cts))
        {
            _logger.LogInformation(
                "Stop generation requested for conversation {ConversationId}",
                conversationId);
            cts.Cancel();
            return Task.CompletedTask;
        }

        _logger.LogDebug(
            "No active stream found for conversation {ConversationId}",
            conversationId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get suggested questions for a notice.
    /// </summary>
    public async Task<List<string>> GetSuggestedQuestions(Guid noticeId)
    {
        return await _aiChatService.GetSuggestedQuestionsAsync(noticeId);
    }

    /// <summary>
    /// Notify other clients about a new message or update.
    /// </summary>
    public async Task NotifyMessageUpdate(Guid conversationId, MessageDto message)
    {
        var groupName = GetConversationGroup(conversationId);
        await Clients.OthersInGroup(groupName).SendAsync("MessageReceived", message);
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        _logger.LogDebug(
            "Client {ConnectionId} connected, UserId: {UserId}",
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        _logger.LogDebug(
            "Client {ConnectionId} disconnected, UserId: {UserId}, Exception: {Exception}",
            Context.ConnectionId, userId, exception?.Message);

        // Clean up any active streams for this connection
        var keysToRemove = _activeStreams.Keys
            .Where(k => k.StartsWith(Context.ConnectionId))
            .ToList();

        foreach (var key in keysToRemove)
        {
            if (_activeStreams.TryRemove(key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastToOthersAsync(Guid conversationId, ChatStreamEvent evt)
    {
        var groupName = GetConversationGroup(conversationId);
        await Clients.OthersInGroup(groupName).SendAsync("ChatEvent", evt);
    }

    private Guid? GetUserId()
    {
        var claim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirst("sub");
        return Guid.TryParse(claim?.Value, out var id) ? id : null;
    }

    private static string GetConversationGroup(Guid conversationId)
    {
        return $"chat:{conversationId}";
    }

    private string GetStreamKey(Guid conversationId)
    {
        return $"{Context.ConnectionId}:{conversationId}";
    }
}
