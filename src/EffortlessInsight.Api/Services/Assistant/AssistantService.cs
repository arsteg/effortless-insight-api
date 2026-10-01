using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.Assistant;

public class AssistantService : IAssistantService
{
    public const string HttpClientName = "Assistant";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISubscriptionService _subscriptionService;
    private readonly AssistantOptions _options;
    private readonly AiServiceOptions _aiOptions;
    private readonly ILogger<AssistantService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AssistantService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ISubscriptionService subscriptionService,
        IOptions<AssistantOptions> options,
        IOptions<AiServiceOptions> aiOptions,
        ILogger<AssistantService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _subscriptionService = subscriptionService;
        _options = options.Value;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------ //
    // Conversation CRUD
    // ------------------------------------------------------------------ //

    public async Task<AssistantConversationListDto> GetConversationsAsync(
        Guid organizationId, Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.AssistantConversations
            .Where(c => c.OrganizationId == organizationId && c.UserId == userId)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new AssistantConversationDto(
                c.Id, c.Title, c.Status, c.Platform, c.MessageCount, c.LastMessageAt, c.CreatedAt))
            .ToListAsync(ct);

        return new AssistantConversationListDto(items, total, page, pageSize);
    }

    public async Task<AssistantConversationDetailDto> CreateConversationAsync(
        Guid organizationId, Guid userId, CreateAssistantConversationRequest request, CancellationToken ct = default)
    {
        var conversation = new AssistantConversation
        {
            OrganizationId = organizationId,
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "New Conversation" : request.Title.Trim(),
            Platform = request.Platform == "mobile" ? "mobile" : "web",
        };

        _db.AssistantConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        return ToDetailDto(conversation, []);
    }

    public async Task<AssistantConversationDetailDto?> GetConversationAsync(
        Guid conversationId, Guid organizationId, Guid userId, int messageLimit, CancellationToken ct = default)
    {
        var conversation = await FindConversationAsync(conversationId, organizationId, userId, ct);
        if (conversation == null) return null;

        messageLimit = Math.Clamp(messageLimit, 1, 200);
        var messages = await _db.AssistantMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(messageLimit)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return ToDetailDto(conversation, messages);
    }

    public async Task<bool> RenameConversationAsync(
        Guid conversationId, Guid organizationId, Guid userId, string title, CancellationToken ct = default)
    {
        var conversation = await FindConversationAsync(conversationId, organizationId, userId, ct);
        if (conversation == null) return false;

        conversation.Title = title.Trim();
        conversation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteConversationAsync(
        Guid conversationId, Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        var conversation = await FindConversationAsync(conversationId, organizationId, userId, ct);
        if (conversation == null) return false;

        conversation.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ------------------------------------------------------------------ //
    // Chat turns
    // ------------------------------------------------------------------ //

    public async Task<AssistantTurnDto> SendMessageAsync(
        AssistantTurnContext context, SendAssistantMessageRequest request, CancellationToken ct = default)
    {
        var conversation = await RequireConversationAsync(context, ct);
        var userMessage = await PersistUserMessageAsync(conversation, request.Content, ct);

        var payload = await BuildTurnPayloadAsync(conversation, context, request, ct);
        using var httpRequest = await BuildChatRequestAsync("api/v1/assistant/chat", payload, context);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await PersistAssistantErrorAsync(conversation, ct);
            throw new AssistantUnavailableException("AI service unreachable", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                await PersistAssistantErrorAsync(conversation, ct);
                _logger.LogWarning("Assistant chat turn failed with status {Status}", (int)response.StatusCode);
                throw new AssistantUnavailableException($"AI service returned {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var assistantMessage = await PersistAssistantMessageAsync(
                conversation,
                content: root.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "",
                citations: ReadStringList(root, "citations"),
                actionsJson: ReadRawArray(root, "actions"),
                toolCallsJson: ReadRawArray(root, "toolCalls"),
                tokenCount: root.TryGetProperty("tokenCount", out var t) ? t.GetInt32() : 0,
                modelId: root.TryGetProperty("model", out var m) ? m.GetString() : null,
                ct: ct);

            return new AssistantTurnDto(ToMessageDto(userMessage), ToMessageDto(assistantMessage));
        }
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        AssistantTurnContext context,
        SendAssistantMessageRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var conversation = await RequireConversationAsync(context, ct);
        var userMessage = await PersistUserMessageAsync(conversation, request.Content, ct);

        yield return Sse(new { type = "user_message_saved", messageId = userMessage.Id });

        var payload = await BuildTurnPayloadAsync(conversation, context, request, ct);
        using var httpRequest = await BuildChatRequestAsync("api/v1/assistant/chat/stream", payload, context);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage? response = null;
        Exception? connectError = null;
        try
        {
            response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                connectError = new AssistantUnavailableException($"AI service returned {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            connectError = ex;
        }

        if (connectError != null)
        {
            response?.Dispose();
            await PersistAssistantErrorAsync(conversation, ct);
            _logger.LogWarning(connectError, "Assistant stream could not start");
            yield return Sse(new { type = "error", message = "assistant_unavailable" });
            yield return "data: [DONE]";
            yield break;
        }

        var contentBuilder = new StringBuilder();
        var citations = new List<string>();
        string? actionsJson = null, toolCallsJson = null, modelId = null;
        var tokenCount = 0;
        var sawError = false;
        var completed = false;

        using (response)
        await using (var stream = await response!.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream))
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;

                var data = line["data: ".Length..];
                if (data == "[DONE]")
                    break;

                ParseStreamEvent(
                    data, contentBuilder, citations,
                    ref actionsJson, ref toolCallsJson, ref modelId, ref tokenCount,
                    ref sawError, ref completed);

                yield return line;
            }
        }

        if (sawError || !completed)
        {
            await PersistAssistantErrorAsync(conversation, ct);
            if (!sawError)
                yield return Sse(new { type = "error", message = "assistant_unavailable" });
        }
        else
        {
            var assistantMessage = await PersistAssistantMessageAsync(
                conversation, contentBuilder.ToString(), citations,
                actionsJson, toolCallsJson, tokenCount, modelId, ct);
            yield return Sse(new { type = "assistant_message_saved", messageId = assistantMessage.Id });
        }

        yield return "data: [DONE]";
    }

    public async Task<AssistantTranscriptionDto> TranscribeAsync(
        AssistantTurnContext context, Stream audio, string fileName, string contentType, long length,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(audio);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "audio.webm" : fileName);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/assistant/transcribe")
        {
            Content = form,
        };
        await AddForwardingHeadersAsync(httpRequest, context);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AssistantUnavailableException("AI service unreachable", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AssistantUnavailableException($"Transcription failed ({(int)response.StatusCode})");

            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<AssistantTranscriptionDto>(body, JsonOptions)
                ?? throw new AssistantUnavailableException("Unreadable transcription response");
        }
    }

    // ------------------------------------------------------------------ //
    // Internals
    // ------------------------------------------------------------------ //

    private Task<AssistantConversation?> FindConversationAsync(
        Guid conversationId, Guid organizationId, Guid userId, CancellationToken ct) =>
        _db.AssistantConversations.FirstOrDefaultAsync(
            c => c.Id == conversationId && c.OrganizationId == organizationId && c.UserId == userId, ct);

    private async Task<AssistantConversation> RequireConversationAsync(
        AssistantTurnContext context, CancellationToken ct) =>
        await FindConversationAsync(context.ConversationId, context.OrganizationId, context.UserId, ct)
            ?? throw new KeyNotFoundException("Conversation not found");

    private async Task<AssistantMessage> PersistUserMessageAsync(
        AssistantConversation conversation, string content, CancellationToken ct)
    {
        var message = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Role = AssistantMessageRole.User,
            Content = content,
        };
        _db.AssistantMessages.Add(message);

        conversation.MessageCount++;
        conversation.LastMessageAt = DateTime.UtcNow;
        if (conversation.MessageCount == 1 && conversation.Title == "New Conversation")
        {
            conversation.Title = content.Length <= 60 ? content : content[..57] + "...";
        }

        await _db.SaveChangesAsync(ct);
        return message;
    }

    private async Task<AssistantMessage> PersistAssistantMessageAsync(
        AssistantConversation conversation, string content, List<string> citations,
        string? actionsJson, string? toolCallsJson, int tokenCount, string? modelId,
        CancellationToken ct)
    {
        var message = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Role = AssistantMessageRole.Assistant,
            Content = content,
            Citations = citations,
            ActionsJson = actionsJson,
            ToolCallsJson = toolCallsJson,
            TokenCount = tokenCount,
            ModelId = modelId,
        };
        _db.AssistantMessages.Add(message);

        conversation.MessageCount++;
        conversation.TotalTokens += tokenCount;
        conversation.LastMessageAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return message;
    }

    private async Task PersistAssistantErrorAsync(AssistantConversation conversation, CancellationToken ct)
    {
        var message = new AssistantMessage
        {
            ConversationId = conversation.Id,
            Role = AssistantMessageRole.Assistant,
            Content = "Sorry — the assistant is temporarily unavailable. Please try again.",
            IsError = true,
        };
        _db.AssistantMessages.Add(message);
        conversation.MessageCount++;
        conversation.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<object> BuildTurnPayloadAsync(
        AssistantConversation conversation, AssistantTurnContext context,
        SendAssistantMessageRequest request, CancellationToken ct)
    {
        var window = await _db.AssistantMessages
            .Where(m => m.ConversationId == conversation.Id && !m.IsError)
            .OrderByDescending(m => m.CreatedAt)
            .Take(_options.HistoryWindowMessages)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { role = m.Role, content = m.Content })
            .ToListAsync(ct);

        return new
        {
            messages = window,
            platform = context.Platform,
            context = request.Context == null
                ? null
                : new { route = request.Context.Route, noticeId = request.Context.NoticeId },
        };
    }

    private async Task<HttpRequestMessage> BuildChatRequestAsync(
        string path, object payload, AssistantTurnContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        await AddForwardingHeadersAsync(request, context);
        return request;
    }

    private async Task AddForwardingHeadersAsync(HttpRequestMessage request, AssistantTurnContext context)
    {
        if (!string.IsNullOrEmpty(_aiOptions.ApiKey))
            request.Headers.Add("X-API-Key", _aiOptions.ApiKey);

        request.Headers.Add("X-Organization-Id", context.OrganizationId.ToString());
        request.Headers.Add("X-User-Id", context.UserId.ToString());
        request.Headers.Add("X-User-Role", context.UserRole);

        var planCode = await ResolvePlanCodeAsync(context.OrganizationId);
        if (!string.IsNullOrEmpty(planCode))
            request.Headers.Add("X-Plan", planCode);

        // The user's own JWT — lets the AI service execute read tools AS the user,
        // inheriting every tenant/role/feature check. Never logged, never stored.
        if (!string.IsNullOrEmpty(context.BearerToken))
            request.Headers.Add("X-Forwarded-Authorization", context.BearerToken);
    }

    private async Task<string?> ResolvePlanCodeAsync(Guid organizationId)
    {
        try
        {
            var subscription = await _subscriptionService.GetSubscriptionEntityAsync(organizationId);
            return subscription?.PlanCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Plan lookup failed for assistant turn; proceeding without X-Plan");
            return null;
        }
    }

    private static void ParseStreamEvent(
        string data, StringBuilder content, List<string> citations,
        ref string? actionsJson, ref string? toolCallsJson, ref string? modelId,
        ref int tokenCount, ref bool sawError, ref bool completed)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            switch (type)
            {
                case "content_chunk":
                    if (root.TryGetProperty("content", out var chunk))
                        content.Append(chunk.GetString());
                    break;
                case "stream_completed":
                    completed = true;
                    if (root.TryGetProperty("content", out var full) && full.GetString() is { Length: > 0 } text)
                    {
                        content.Clear();
                        content.Append(text);
                    }
                    citations.AddRange(ReadStringList(root, "citations"));
                    actionsJson = ReadRawArray(root, "actions") ?? actionsJson;
                    toolCallsJson = ReadRawArray(root, "toolCalls") ?? toolCallsJson;
                    if (root.TryGetProperty("tokenCount", out var tokens)) tokenCount = tokens.GetInt32();
                    if (root.TryGetProperty("model", out var model)) modelId = model.GetString();
                    break;
                case "error":
                    sawError = true;
                    break;
            }
        }
        catch (JsonException)
        {
            // Pass unparseable lines through untouched; they just don't affect persistence.
        }
    }

    private static List<string> ReadStringList(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
            return [];
        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }

    private static string? ReadRawArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array)
            return null;
        return element.GetArrayLength() == 0 ? null : element.GetRawText();
    }

    private static string Sse(object payload) =>
        "data: " + JsonSerializer.Serialize(payload, JsonOptions);

    private static AssistantMessageDto ToMessageDto(AssistantMessage message)
    {
        JsonElement? actions = null;
        if (!string.IsNullOrEmpty(message.ActionsJson))
        {
            using var document = JsonDocument.Parse(message.ActionsJson);
            actions = document.RootElement.Clone();
        }
        return new AssistantMessageDto(
            message.Id, message.Role, message.Content, message.Citations,
            actions, message.TokenCount, message.ModelId, message.IsError, message.CreatedAt);
    }

    private static AssistantConversationDetailDto ToDetailDto(
        AssistantConversation conversation, List<AssistantMessage> messages) =>
        new(
            conversation.Id, conversation.Title, conversation.Status, conversation.Platform,
            conversation.MessageCount, conversation.LastMessageAt, conversation.CreatedAt,
            messages.Select(ToMessageDto).ToList());
}
