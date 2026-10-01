using System.Text.Json;
using System.Text.Json.Serialization;

namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// App-wide assistant DTOs (gateway for the AI-service assistant engine)
// ============================================================================

public record AssistantConversationDto(
    Guid Id,
    string Title,
    string Status,
    string Platform,
    int MessageCount,
    DateTime? LastMessageAt,
    DateTime CreatedAt);

public record AssistantMessageDto(
    Guid Id,
    string Role,
    string Content,
    List<string> Citations,
    JsonElement? Actions,
    int TokenCount,
    string? ModelId,
    bool IsError,
    DateTime CreatedAt);

public record AssistantConversationDetailDto(
    Guid Id,
    string Title,
    string Status,
    string Platform,
    int MessageCount,
    DateTime? LastMessageAt,
    DateTime CreatedAt,
    List<AssistantMessageDto> Messages);

public record AssistantConversationListDto(
    List<AssistantConversationDto> Conversations,
    int TotalCount,
    int Page,
    int PageSize);

public record CreateAssistantConversationRequest(
    string? Title,
    string? Platform);

public record UpdateAssistantConversationRequest(string Title);

/// <summary>Client context sent with each turn for grounded answers.</summary>
public record AssistantClientContext(
    string? Route,
    Guid? NoticeId);

public record SendAssistantMessageRequest(
    string Content,
    AssistantClientContext? Context);

/// <summary>Result of a non-streaming chat turn.</summary>
public record AssistantTurnDto(
    AssistantMessageDto UserMessage,
    AssistantMessageDto AssistantMessage);

public record AssistantTranscriptionDto(
    string Text,
    string? Language,
    [property: JsonPropertyName("durationSeconds")] double? DurationSeconds);
