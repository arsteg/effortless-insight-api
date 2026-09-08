using System.Net;
using EffortlessInsight.Api.Services.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq.Protected;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class TwoFactorSmsServiceTests
{
    private Uri? _requestedUri;

    private TwoFactorSmsService CreateService(
        string apiKey = "test-api-key",
        string templateName = "",
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string responseBody = """{"Status":"Success","Details":"session-id"}""")
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _requestedUri = req.RequestUri)
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Sms:TwoFactor:ApiKey", apiKey },
                { "Sms:TwoFactor:BaseUrl", "https://2factor.in/API/V1" },
                { "Sms:TwoFactor:OtpTemplateName", templateName },
                { "Sms:TwoFactor:CountryCode", "91" }
            })
            .Build();

        return new TwoFactorSmsService(
            new HttpClient(handler.Object),
            configuration,
            new Mock<ILogger<TwoFactorSmsService>>().Object);
    }

    [Fact]
    public async Task SendOtpAsync_ShouldCallCustomOtpEndpoint()
    {
        var service = CreateService();

        await service.SendOtpAsync("9876543210", "123456", 5);

        _requestedUri.Should().NotBeNull();
        _requestedUri!.AbsoluteUri.Should().Be(
            "https://2factor.in/API/V1/test-api-key/SMS/%2B919876543210/123456");
    }

    [Fact]
    public async Task SendOtpAsync_WithTemplate_ShouldAppendTemplateName()
    {
        var service = CreateService(templateName: "MYOTP");

        await service.SendOtpAsync("9876543210", "123456", 5);

        _requestedUri!.AbsoluteUri.Should().EndWith("/123456/MYOTP");
    }

    [Fact]
    public async Task SendOtpAsync_WhenProviderRejects_ShouldThrowGenericError()
    {
        var service = CreateService(
            responseBody: """{"Status":"Error","Details":"Invalid API Key"}""");

        var act = () => service.SendOtpAsync("9876543210", "123456", 5);

        // Provider details must never leak to callers
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("SMS_SEND_FAILED");
    }

    [Fact]
    public async Task SendOtpAsync_WhenHttpFails_ShouldThrowGenericError()
    {
        var service = CreateService(statusCode: HttpStatusCode.InternalServerError);

        var act = () => service.SendOtpAsync("9876543210", "123456", 5);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("SMS_SEND_FAILED");
    }

    [Fact]
    public async Task SendOtpAsync_WithoutApiKey_ShouldFailWithoutCallingProvider()
    {
        var service = CreateService(apiKey: "");

        var act = () => service.SendOtpAsync("9876543210", "123456", 5);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("SMS_SEND_FAILED");
        _requestedUri.Should().BeNull();
    }

    [Fact]
    public async Task SendSmsAsync_ShouldNotBeSupported()
    {
        var service = CreateService();

        var act = () => service.SendSmsAsync("9876543210", "free text");

        await act.Should().ThrowAsync<NotSupportedException>();
        _requestedUri.Should().BeNull();
    }
}
