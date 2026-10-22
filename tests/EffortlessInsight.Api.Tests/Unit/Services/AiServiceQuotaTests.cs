using System.Net;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class AiServiceQuotaTests
{
    private sealed class QuotaHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) {
                Content = new StringContent("""{"code":"USAGE_CAP_EXCEEDED","error":"Monthly AI usage limit reached (50/50).","used":50,"cap":50}""")
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MonthlyQuota_IsNotRetried_AndPreservesErrorCode(bool regenerate)
    {
        var handler = new QuotaHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://ai.test") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("AiService")).Returns(client);
        var service = new AiServiceClientImpl(factory.Object, Mock.Of<ISubscriptionService>(),
            Microsoft.Extensions.Options.Options.Create(new AiServiceOptions { MaxRetries = 3, RetryDelaySeconds = 0 }), NullLogger<AiServiceClientImpl>.Instance);
        if (regenerate)
        {
            var result = await service.RegenerateAllegationResponseAsync(Guid.NewGuid(), Guid.Empty,
                new RegenerateAllegationOptions { Allegation = new AllegationData { Id = "ALG-1" } });
            Assert.False(result.Success);
            Assert.Equal("USAGE_CAP_EXCEEDED", result.ErrorCode);
            Assert.Equal("Monthly AI usage limit reached (50/50).", result.Error);
        }
        else
        {
            var result = await service.GenerateAllegationWiseDraftAsync(Guid.NewGuid(), Guid.Empty, new AllegationWiseDraftOptions());
            Assert.False(result.Success);
            Assert.Equal("USAGE_CAP_EXCEEDED", result.ErrorCode);
            Assert.Equal("Monthly AI usage limit reached (50/50).", result.Error);
        }
        Assert.Equal(1, handler.Calls);
    }
}
