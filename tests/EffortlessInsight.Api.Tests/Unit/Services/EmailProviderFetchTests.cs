using System.Net;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.EmailSync;
using Microsoft.Extensions.Logging.Abstractions;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class EmailProviderFetchTests
{
    [Theory]
    [InlineData(403, "{\"error\":{\"errors\":[{\"reason\":\"accessNotConfigured\"}]} }", "gmail_api_disabled")]
    [InlineData(403, "{\"error\":{\"details\":[{\"reason\":\"SERVICE_DISABLED\"}]} }", "gmail_api_disabled")]
    [InlineData(403, "{\"error\":{\"errors\":[{\"reason\":\"insufficientPermissions\"}]} }", "gmail_permission_required")]
    [InlineData(403, "{\"error\":{\"errors\":[{\"reason\":\"userRateLimitExceeded\"}]} }", "provider_rate_limited")]
    [InlineData(403, "{\"error\":{\"errors\":[{\"reason\":\"domainPolicy\"}]} }", "gmail_domain_policy")]
    [InlineData(403, "{}", "gmail_access_denied")]
    [InlineData(401, "{}", "reauthentication_required")]
    [InlineData(429, "", "provider_rate_limited")]
    [InlineData(503, "<html>private provider details</html>", "provider_unavailable")]
    [InlineData(400, "{\"error\":\"private provider details\"}", "provider_failed")]
    public async Task GmailReportsActionableErrorsWithoutAdvancingCheckpoint(int status, string body, string code)
    {
        var client = new GmailProviderClient(Microsoft.Extensions.Options.Options.Create(new EmailSyncOptions()),
            new HttpClient(new FailureHandler(status, body)), NullLogger<GmailProviderClient>.Instance);
        var result = await client.FetchEmailsAsync("access", new() { SenderFilters = ["@gst.gov.in"],
            SyncCursor = new EmailScanCursor(DateTime.UtcNow.Date.AddDays(-1)).Encode() });
        Assert.False(result.Success);
        Assert.Equal(code, result.ErrorCode);
        Assert.Null(result.NextSyncCursor);
        Assert.Empty(result.Messages);
        Assert.DoesNotContain("private provider details", result.ErrorMessage!);
    }

    private sealed class FailureHandler(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task GmailRetainsPageAndNeverFetchesDisallowedBody()
    {
        var handler = new Handler(request =>
        {
            var url = request.RequestUri!.ToString();
            Assert.DoesNotContain("/history", url);
            if (url.Contains("format=metadata")) return "{\"payload\":{\"headers\":[{\"name\":\"From\",\"value\":\"attacker@gst.gov.in.evil.test\"}]}}";
            Assert.Contains("q=", url);
            return "{\"messages\":[{\"id\":\"one\"}],\"nextPageToken\":\"next\"}";
        });
        var client = new GmailProviderClient(Microsoft.Extensions.Options.Options.Create(new EmailSyncOptions()), new HttpClient(handler), NullLogger<GmailProviderClient>.Instance);
        var result = await client.FetchEmailsAsync("access", new() { SenderFilters = ["@gst.gov.in"], MaxResults = 1,
            SyncCursor = new EmailScanCursor(DateTime.UtcNow.Date.AddDays(-1)).Encode() });
        Assert.True(result.Success); Assert.Empty(result.Messages);
        var cursor = JsonSerializer.Deserialize<EmailScanCursor>(result.NextSyncCursor!);
        Assert.Equal("next", cursor!.Page); Assert.NotNull(cursor.Until); Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task MicrosoftFetchesAttachmentMetadataAndCheckpointsRemainingIds()
    {
        var handler = new Handler(request =>
        {
            var url = request.RequestUri!.ToString();
            Assert.DoesNotContain("delta", url);
            if (url.Contains("$search=")) return "{\"value\":[{\"id\":\"one\",\"from\":{\"emailAddress\":{\"address\":\"n@gst.gov.in\"}}},{\"id\":\"two\",\"from\":{\"emailAddress\":{\"address\":\"n@gst.gov.in\"}}}]}";
            if (url.Contains("/attachments?")) return "{\"value\":[{\"id\":\"pdf\",\"name\":\"notice.pdf\",\"contentType\":\"application/pdf\",\"size\":100}]}";
            return "{\"id\":\"one\",\"subject\":\"DRC-01\",\"from\":{\"emailAddress\":{\"address\":\"n@gst.gov.in\"}},\"hasAttachments\":true,\"body\":{\"contentType\":\"text\",\"content\":\"notice\"}}";
        });
        var client = new OutlookProviderClient(Microsoft.Extensions.Options.Options.Create(new EmailSyncOptions()), new HttpClient(handler), NullLogger<OutlookProviderClient>.Instance);
        var result = await client.FetchEmailsAsync("access", new() { SenderFilters = ["@gst.gov.in"], MaxResults = 1,
            SyncCursor = new EmailScanCursor(DateTime.UtcNow.Date.AddDays(-1)).Encode() });
        Assert.True(result.Success); Assert.Single(result.Messages); Assert.Single(result.Messages[0].Attachments);
        Assert.Equal("notice", result.Messages[0].BodyText);
        Assert.Equal(["two"], JsonSerializer.Deserialize<EmailScanCursor>(result.NextSyncCursor!)!.PendingIds);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task EmptyAllowlistNeverMakesMailboxRequest()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("Unexpected mailbox request"));
        var client = new OutlookProviderClient(Microsoft.Extensions.Options.Options.Create(new EmailSyncOptions()), new HttpClient(handler), NullLogger<OutlookProviderClient>.Instance);
        Assert.False((await client.FetchEmailsAsync("access", new())).Success);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class Handler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
        }
    }
}
