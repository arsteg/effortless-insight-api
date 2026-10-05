using System.Net;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Assistant;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class AssistantServiceTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    // ------------------------------------------------------------------ //
    // Test plumbing
    // ------------------------------------------------------------------ //

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private AssistantService CreateService(
        ApplicationDbContext db,
        CapturingHandler handler,
        int historyWindow = 20)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://ai.test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AssistantService.HttpClientName)).Returns(client);

        var subscriptions = new Mock<ISubscriptionService>();
        subscriptions
            .Setup(s => s.GetSubscriptionEntityAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new BillingSubscription { PlanCode = "team" });

        return new AssistantService(
            db,
            factory.Object,
            subscriptions.Object,
            Microsoft.Extensions.Options.Options.Create(new AssistantOptions
            {
                HistoryWindowMessages = historyWindow,
            }),
            Microsoft.Extensions.Options.Options.Create(new AiServiceOptions
            {
                ApiKey = "service-api-key",
            }),
            NullLogger<AssistantService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(object payload, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    private async Task<AssistantConversation> SeedConversationAsync(ApplicationDbContext db)
    {
        var conversation = new AssistantConversation
        {
            OrganizationId = _orgId,
            UserId = _userId,
        };
        db.AssistantConversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private AssistantTurnContext TurnContext(Guid conversationId) =>
        new(conversationId, _orgId, _userId, "member", "web", "Bearer user-jwt");

    private static readonly object HappyChatResponse = new
    {
        content = "You have 3 notices.",
        citations = new[] { "EI-W01" },
        actions = new object[] { new { type = "navigate", intent = "notices_list", label = "Notices" } },
        toolCalls = new object[] { new { tool = "get_notices", status = "ok" } },
        model = "gpt-4o-mini",
        tokenCount = 123,
    };

    // ------------------------------------------------------------------ //
    // Conversation CRUD + tenant/user scoping
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Conversations_are_scoped_to_org_and_user()
    {
        using var db = BillingTestDbContextFactory.Create();
        var handler = new CapturingHandler(_ => JsonResponse(HappyChatResponse));
        var service = CreateService(db, handler);

        await service.CreateConversationAsync(_orgId, _userId, new CreateAssistantConversationRequest(null, "web"));
        await service.CreateConversationAsync(_orgId, Guid.NewGuid(), new CreateAssistantConversationRequest(null, "web"));
        await service.CreateConversationAsync(Guid.NewGuid(), _userId, new CreateAssistantConversationRequest(null, "web"));

        var mine = await service.GetConversationsAsync(_orgId, _userId, 1, 20);
        mine.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Get_rename_delete_do_not_cross_users()
    {
        using var db = BillingTestDbContextFactory.Create();
        var service = CreateService(db, new CapturingHandler(_ => JsonResponse(HappyChatResponse)));
        var conversation = await SeedConversationAsync(db);
        var otherUser = Guid.NewGuid();

        (await service.GetConversationAsync(conversation.Id, _orgId, otherUser, 50)).Should().BeNull();
        (await service.RenameConversationAsync(conversation.Id, _orgId, otherUser, "hack")).Should().BeFalse();
        (await service.DeleteConversationAsync(conversation.Id, _orgId, otherUser)).Should().BeFalse();

        (await service.DeleteConversationAsync(conversation.Id, _orgId, _userId)).Should().BeTrue();
        (await service.GetConversationAsync(conversation.Id, _orgId, _userId, 50)).Should().BeNull("soft-deleted");
    }

    // ------------------------------------------------------------------ //
    // Sync turn
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task SendMessage_persists_both_messages_and_returns_turn()
    {
        using var db = BillingTestDbContextFactory.Create();
        var handler = new CapturingHandler(_ => JsonResponse(HappyChatResponse));
        var service = CreateService(db, handler);
        var conversation = await SeedConversationAsync(db);

        var turn = await service.SendMessageAsync(
            TurnContext(conversation.Id),
            new SendAssistantMessageRequest("show my notices", null));

        turn.UserMessage.Role.Should().Be("user");
        turn.AssistantMessage.Content.Should().Be("You have 3 notices.");
        turn.AssistantMessage.Citations.Should().ContainSingle("EI-W01");
        turn.AssistantMessage.Actions.Should().NotBeNull();

        var updated = db.AssistantConversations.Single(c => c.Id == conversation.Id);
        updated.MessageCount.Should().Be(2);
        updated.TotalTokens.Should().Be(123);
        updated.Title.Should().Be("show my notices", "auto-titled from the first message");
        db.AssistantMessages.Count(m => m.ConversationId == conversation.Id).Should().Be(2);
    }

    [Fact]
    public async Task SendMessage_forwards_identity_headers_and_user_jwt()
    {
        using var db = BillingTestDbContextFactory.Create();
        var handler = new CapturingHandler(_ => JsonResponse(HappyChatResponse));
        var service = CreateService(db, handler);
        var conversation = await SeedConversationAsync(db);

        await service.SendMessageAsync(
            TurnContext(conversation.Id),
            new SendAssistantMessageRequest("hello", new AssistantClientContext("/notices", null)));

        var request = handler.LastRequest!;
        request.Headers.GetValues("X-API-Key").Single().Should().Be("service-api-key");
        request.Headers.GetValues("X-Organization-Id").Single().Should().Be(_orgId.ToString());
        request.Headers.GetValues("X-User-Id").Single().Should().Be(_userId.ToString());
        request.Headers.GetValues("X-User-Role").Single().Should().Be("member");
        request.Headers.GetValues("X-Plan").Single().Should().Be("team");
        request.Headers.GetValues("X-Forwarded-Authorization").Single().Should().Be("Bearer user-jwt");

        // the user's JWT must never leak into the JSON body sent to the AI service
        handler.LastRequestBody.Should().NotContain("user-jwt");
        handler.LastRequestBody.Should().Contain("\"platform\":\"web\"");
        handler.LastRequestBody.Should().Contain("/notices");
    }

    [Fact]
    public async Task SendMessage_sends_only_the_history_window()
    {
        using var db = BillingTestDbContextFactory.Create();
        var handler = new CapturingHandler(_ => JsonResponse(HappyChatResponse));
        var service = CreateService(db, handler, historyWindow: 3);
        var conversation = await SeedConversationAsync(db);

        for (var i = 0; i < 6; i++)
        {
            db.AssistantMessages.Add(new AssistantMessage
            {
                ConversationId = conversation.Id,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"old-{i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10 + i),
            });
        }
        await db.SaveChangesAsync();

        await service.SendMessageAsync(
            TurnContext(conversation.Id), new SendAssistantMessageRequest("newest", null));

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(3);
        messages[2].GetProperty("content").GetString().Should().Be("newest");
        handler.LastRequestBody.Should().NotContain("old-0");
    }

    [Fact]
    public async Task SendMessage_when_ai_service_down_persists_error_and_throws()
    {
        using var db = BillingTestDbContextFactory.Create();
        var handler = new CapturingHandler(_ => throw new HttpRequestException("down"));
        var service = CreateService(db, handler);
        var conversation = await SeedConversationAsync(db);

        var act = () => service.SendMessageAsync(
            TurnContext(conversation.Id), new SendAssistantMessageRequest("hi", null));

        await act.Should().ThrowAsync<AssistantUnavailableException>();
        db.AssistantMessages
            .Count(m => m.ConversationId == conversation.Id && m.IsError)
            .Should().Be(1);
    }

    [Fact]
    public async Task SendMessage_unknown_conversation_throws_not_found()
    {
        using var db = BillingTestDbContextFactory.Create();
        var service = CreateService(db, new CapturingHandler(_ => JsonResponse(HappyChatResponse)));

        var act = () => service.SendMessageAsync(
            TurnContext(Guid.NewGuid()), new SendAssistantMessageRequest("hi", null));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ //
    // Streaming turn
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Stream_passes_through_events_and_persists_final_message()
    {
        using var db = BillingTestDbContextFactory.Create();
        var sse = string.Join("\n\n", new[]
        {
            "data: {\"type\": \"stream_started\"}",
            "data: {\"type\": \"content_chunk\", \"content\": \"Hello \"}",
            "data: {\"type\": \"content_chunk\", \"content\": \"there\"}",
            "data: {\"type\": \"stream_completed\", \"content\": \"Hello there\", \"citations\": [\"EI-W01\"], \"actions\": [], \"toolCalls\": [], \"model\": \"gpt-4o-mini\", \"tokenCount\": 55}",
            "data: [DONE]",
        }) + "\n\n";
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });
        var service = CreateService(db, handler);
        var conversation = await SeedConversationAsync(db);

        var lines = new List<string>();
        await foreach (var line in service.StreamMessageAsync(
            TurnContext(conversation.Id), new SendAssistantMessageRequest("hi", null)))
        {
            lines.Add(line);
        }

        lines.First().Should().Contain("user_message_saved");
        lines.Should().Contain(l => l.Contains("content_chunk"));
        lines.Should().Contain(l => l.Contains("assistant_message_saved"));
        lines.Last().Should().Be("data: [DONE]");

        var saved = db.AssistantMessages
            .Single(m => m.ConversationId == conversation.Id && m.Role == "assistant");
        saved.Content.Should().Be("Hello there");
        saved.TokenCount.Should().Be(55);
        saved.ModelId.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task Stream_when_ai_service_down_emits_error_and_done()
    {
        using var db = BillingTestDbContextFactory.Create();
        var handler = new CapturingHandler(_ => throw new HttpRequestException("down"));
        var service = CreateService(db, handler);
        var conversation = await SeedConversationAsync(db);

        var lines = new List<string>();
        await foreach (var line in service.StreamMessageAsync(
            TurnContext(conversation.Id), new SendAssistantMessageRequest("hi", null)))
        {
            lines.Add(line);
        }

        lines.Should().Contain(l => l.Contains("\"error\""));
        lines.Last().Should().Be("data: [DONE]");
        db.AssistantMessages
            .Count(m => m.ConversationId == conversation.Id && m.IsError)
            .Should().Be(1);
    }
}
