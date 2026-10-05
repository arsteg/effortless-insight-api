using EffortlessInsight.Api.Controllers;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Assistant;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Controllers;

/// <summary>
/// The assistant costs real money per turn — plans without AI features
/// (free "Notify Only") must be rejected server-side, not just hidden in UI.
/// </summary>
public class AssistantControllerFeatureGateTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IAssistantService> _assistantService = new();
    private readonly Mock<IAssistantRateLimiter> _rateLimiter = new();
    private readonly Mock<IFeatureAccessService> _featureAccess = new();

    private AssistantController CreateController(bool hasAiFeature)
    {
        _featureAccess
            .Setup(f => f.HasFeatureAccessAsync(_orgId, FeatureCodes.AskAi, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasAiFeature);

        var currentOrg = new Mock<ICurrentOrganizationService>();
        currentOrg.SetupGet(c => c.OrganizationId).Returns(_orgId);
        currentOrg.SetupGet(c => c.UserId).Returns(_userId);
        currentOrg.SetupGet(c => c.Role).Returns("owner");

        var controller = new AssistantController(
            _assistantService.Object,
            _rateLimiter.Object,
            currentOrg.Object,
            _featureAccess.Object,
            Microsoft.Extensions.Options.Options.Create(new AssistantOptions()),
            NullLogger<AssistantController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        return controller;
    }

    [Fact]
    public async Task SendMessageSync_without_ai_feature_returns_403_and_never_calls_service()
    {
        var controller = CreateController(hasAiFeature: false);

        var result = await controller.SendMessageSync(
            Guid.NewGuid(), new SendAssistantMessageRequest("hi", null));

        var status = result.Should().BeOfType<ObjectResult>().Which;
        status.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        status.Value.Should().BeOfType<ApiErrorResponse>()
            .Which.Code.Should().Be("FEATURE_NOT_AVAILABLE");
        _assistantService.Verify(
            s => s.SendMessageAsync(It.IsAny<AssistantTurnContext>(), It.IsAny<SendAssistantMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendMessageSync_with_ai_feature_proceeds()
    {
        var controller = CreateController(hasAiFeature: true);
        var message = new AssistantMessageDto(
            Guid.NewGuid(), "assistant", "hello", [], null, 0, null, false, DateTime.UtcNow);
        _assistantService
            .Setup(s => s.SendMessageAsync(It.IsAny<AssistantTurnContext>(), It.IsAny<SendAssistantMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssistantTurnDto(message, message));

        var result = await controller.SendMessageSync(
            Guid.NewGuid(), new SendAssistantMessageRequest("hi", null));

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Stream_without_ai_feature_returns_403_before_any_sse()
    {
        var controller = CreateController(hasAiFeature: false);
        controller.HttpContext.Response.Body = new MemoryStream();

        await controller.SendMessageStream(Guid.NewGuid(), new SendAssistantMessageRequest("hi", null));

        controller.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        controller.HttpContext.Response.ContentType.Should().NotBe("text/event-stream");
        _assistantService.Verify(
            s => s.StreamMessageAsync(It.IsAny<AssistantTurnContext>(), It.IsAny<SendAssistantMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
    [Fact]
    public async Task All_conversation_endpoints_and_voice_require_AskAi()
    {
        var controller = CreateController(hasAiFeature: false);
        // A separate AI entitlement must not unlock Ask AI.
        _featureAccess.Setup(f => f.HasFeatureAccessAsync(
            _orgId, FeatureCodes.AiExplanation, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var id = Guid.NewGuid();
        IActionResult[] results =
        [
            await controller.GetConversations(),
            await controller.CreateConversation(null!),
            await controller.GetConversation(id),
            await controller.RenameConversation(id, null!),
            await controller.DeleteConversation(id),
            await controller.Transcribe(null!),
        ];
        foreach (var result in results)
            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        _assistantService.Invocations.Should().BeEmpty();
    }
}
