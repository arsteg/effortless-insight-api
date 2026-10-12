using System.Net;
using System.Text;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.EmailSync;
using Microsoft.Extensions.Logging.Abstractions;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class EmailOAuthErrorTests
{
    [Fact]
    public void MicrosoftConnectionAlwaysOffersAccountSelection()
    {
        var url = new Uri(Client("{}").GetAuthorizationUrl("state", "https://api.test/callback", "challenge"));
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("select_account", query["prompt"]);
        Assert.Equal("S256", query["code_challenge_method"]);
    }

    [Theory]
    [InlineData(7000215, "client_secret_invalid")]
    [InlineData(7000222, "client_secret_expired")]
    [InlineData(9002331, "provider_audience_mismatch")]
    public async Task MicrosoftErrorsUseSafeActionableCodes(int aadCode, string expected)
    {
        var body = $$"""{"error":"invalid_client","error_codes":[{{aadCode}}],"error_description":"sensitive-provider-description"}""";
        var client = Client(body);
        var result = await client.ExchangeCodeAsync("code", "https://api.test/callback", "verifier");
        Assert.False(result.Success);
        Assert.Equal(expected, result.ErrorCode);
        Assert.DoesNotContain("sensitive-provider-description", result.ErrorMessage);
        var refreshed = await client.RefreshTokenAsync("refresh");
        Assert.Equal(expected, refreshed.ErrorCode);
    }

    [Theory]
    [InlineData("{\"error\":\"private-token-value\",\"error_description\":\"sensitive\"}")]
    [InlineData("not json")]
    public async Task UnknownOrMalformedErrorsNeverExposeResponseData(string body)
    {
        var result = await Client(body).ExchangeCodeAsync("code", "https://api.test/callback", "verifier");
        Assert.Equal("token_exchange_failed", result.ErrorCode);
        Assert.DoesNotContain("private-token-value", result.ErrorMessage);
        Assert.DoesNotContain("sensitive", result.ErrorMessage);
    }

    private static OutlookProviderClient Client(string body) => new(
        Microsoft.Extensions.Options.Options.Create(new EmailSyncOptions()),
        new HttpClient(new Handler(body)), NullLogger<OutlookProviderClient>.Instance);

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
