using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Assistant;

/// <summary>
/// Gateway for the app-wide end-user assistant. Owns conversation persistence
/// and proxies chat turns to the Python AI service, forwarding the user's own
/// bearer token so all tool calls run with the user's permissions.
/// </summary>
public interface IAssistantService
{
    Task<AssistantConversationListDto> GetConversationsAsync(
        Guid organizationId, Guid userId, int page, int pageSize, CancellationToken ct = default);

    Task<AssistantConversationDetailDto> CreateConversationAsync(
        Guid organizationId, Guid userId, CreateAssistantConversationRequest request, CancellationToken ct = default);

    Task<AssistantConversationDetailDto?> GetConversationAsync(
        Guid conversationId, Guid organizationId, Guid userId, int messageLimit, CancellationToken ct = default);

    Task<bool> RenameConversationAsync(
        Guid conversationId, Guid organizationId, Guid userId, string title, CancellationToken ct = default);

    Task<bool> DeleteConversationAsync(
        Guid conversationId, Guid organizationId, Guid userId, CancellationToken ct = default);

    /// <summary>Non-streaming chat turn (mobile and fallback path).</summary>
    Task<AssistantTurnDto> SendMessageAsync(
        AssistantTurnContext context, SendAssistantMessageRequest request, CancellationToken ct = default);

    /// <summary>
    /// Streaming chat turn: yields raw SSE "data: ..." lines from the AI service
    /// while persisting the user and assistant messages around the stream.
    /// </summary>
    IAsyncEnumerable<string> StreamMessageAsync(
        AssistantTurnContext context, SendAssistantMessageRequest request, CancellationToken ct = default);

    /// <summary>Proxy speech-to-text to the AI service.</summary>
    Task<AssistantTranscriptionDto> TranscribeAsync(
        AssistantTurnContext context, Stream audio, string fileName, string contentType, long length,
        CancellationToken ct = default);
}

/// <summary>
/// Everything about the asking user that one turn needs. BearerToken is the
/// user's own JWT (raw header value) — forwarded, never logged, never stored.
/// </summary>
public record AssistantTurnContext(
    Guid ConversationId,
    Guid OrganizationId,
    Guid UserId,
    string UserRole,
    string Platform,
    string? BearerToken);

/// <summary>Thrown when the AI service cannot be reached or fails.</summary>
public class AssistantUnavailableException : Exception
{
    public AssistantUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
