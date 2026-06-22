using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EffortlessInsight.Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class AiServiceClientTests
{
    private readonly Mock<ILogger<AiServiceClientImpl>> _loggerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

    public AiServiceClientTests()
    {
        _loggerMock = new Mock<ILogger<AiServiceClientImpl>>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
    }

    private HttpClient CreateMockHttpClient(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };
    }

    [Fact]
    public async Task ProcessNoticeAsync_WithSuccessfulResponse_ReturnsResult()
    {
        // Arrange
        var noticeId = Guid.NewGuid();
        var expectedResult = new AiProcessingResult
        {
            Success = true,
            NoticeId = noticeId,
            NoticeType = "ASMT-10",
            NoticeCategory = "Assessment",
            Summary = "Tax assessment notice",
            RiskScore = 75,
            ActionItems = new List<string> { "Review assessment", "Prepare response" },
            DueDate = DateTime.UtcNow.AddDays(30)
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                data = expectedResult
            })
        };

        var httpClient = CreateMockHttpClient(response);
        _httpClientFactoryMock
            .Setup(f => f.CreateClient("AiService"))
            .Returns(httpClient);

        var service = new AiServiceClientImpl(
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await service.ProcessNoticeAsync(noticeId, "https://example.com/notice.pdf");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(noticeId, result.NoticeId);
    }

    [Fact]
    public async Task ProcessNoticeAsync_WithFailedResponse_ThrowsException()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\": \"Processing failed\"}")
        };

        var httpClient = CreateMockHttpClient(response);
        _httpClientFactoryMock
            .Setup(f => f.CreateClient("AiService"))
            .Returns(httpClient);

        var service = new AiServiceClientImpl(
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.ProcessNoticeAsync(Guid.NewGuid(), "https://example.com/notice.pdf"));
    }

    [Fact]
    public async Task GenerateResponseDraftAsync_WithSuccessfulResponse_ReturnsDraft()
    {
        // Arrange
        var noticeId = Guid.NewGuid();
        var expectedDraft = new ResponseDraft
        {
            Subject = "Response to ASMT-10 Notice",
            Body = "Dear Sir/Madam,\n\nThis is in response to...",
            RecommendedAttachments = new List<string> { "GSTR-3B returns", "ITC register" }
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                data = expectedDraft
            })
        };

        var httpClient = CreateMockHttpClient(response);
        _httpClientFactoryMock
            .Setup(f => f.CreateClient("AiService"))
            .Returns(httpClient);

        var service = new AiServiceClientImpl(
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await service.GenerateResponseDraftAsync(noticeId, "formal");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Response to ASMT-10 Notice", result.Subject);
        Assert.Contains("response to", result.Body);
    }

    [Fact]
    public async Task FindSimilarNoticesAsync_ReturnsMatchingNotices()
    {
        // Arrange
        var noticeId = Guid.NewGuid();
        var similarNotices = new List<SimilarNoticeResult>
        {
            new() { NoticeId = Guid.NewGuid(), Similarity = 0.92f, Summary = "Similar notice 1" },
            new() { NoticeId = Guid.NewGuid(), Similarity = 0.85f, Summary = "Similar notice 2" }
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                success = true,
                data = new { notices = similarNotices }
            })
        };

        var httpClient = CreateMockHttpClient(response);
        _httpClientFactoryMock
            .Setup(f => f.CreateClient("AiService"))
            .Returns(httpClient);

        var service = new AiServiceClientImpl(
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await service.FindSimilarNoticesAsync(noticeId, 5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.True(result[0].Similarity > result[1].Similarity);
    }

    [Fact]
    public async Task ProcessNoticeAsync_WithTimeout_ThrowsTaskCanceledException()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8000")
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("AiService"))
            .Returns(httpClient);

        var service = new AiServiceClientImpl(
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => service.ProcessNoticeAsync(Guid.NewGuid(), "https://example.com/notice.pdf"));
    }
}

// Test DTOs (simplified versions)
public class AiProcessingResult
{
    public bool Success { get; set; }
    public Guid NoticeId { get; set; }
    public string NoticeType { get; set; } = string.Empty;
    public string NoticeCategory { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int RiskScore { get; set; }
    public List<string> ActionItems { get; set; } = new();
    public DateTime? DueDate { get; set; }
}

public class ResponseDraft
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<string> RecommendedAttachments { get; set; } = new();
}

public class SimilarNoticeResult
{
    public Guid NoticeId { get; set; }
    public float Similarity { get; set; }
    public string Summary { get; set; } = string.Empty;
}
