using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.EmailSync;
using Microsoft.Extensions.Options;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class EmailNoticeInspectorTests
{
    private static readonly EmailMessage Email = new() { Sender = "notice@gst.gov.in", Subject = "GST notice" };

    private static EmailNoticeInspector Inspector(Handler handler, EmailSyncOptions? options = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("EmailInspection")).Returns(new HttpClient(handler));
        return new(factory.Object, Microsoft.Extensions.Options.Options.Create(new AiServiceOptions { BaseUrl = "https://ai.example.test", ApiKey = "test" }),
            Microsoft.Extensions.Options.Options.Create(options ?? new EmailSyncOptions()));
    }

    [Fact]
    public async Task RecognizedPdfUsesExtractionWithoutClassification()
    {
        var handler = new Handler("{\"text\":\"DRC-01 GSTIN 27ABCDE1234F1Z5 Notice No: ABCD12345678\"}");
        var result = await Inspector(handler).InspectAsync("%PDF-test"u8.ToArray(), "application/pdf", Email, Guid.NewGuid(), true, default);
        Assert.True(result.IsNotice);
        Assert.False(result.AiUsed);
        Assert.Equal("ABCD12345678", result.ReferenceNumber);
        Assert.Equal(["/api/v1/email-notices/extract"], handler.Paths);
    }

    [Fact]
    public async Task UncertainContentUsesSingleBoundedClassification()
    {
        var handler = new Handler("{\"isNotice\":false,\"confidence\":0.98}");
        var result = await Inspector(handler).InspectAsync(Encoding.UTF8.GetBytes("GST Notice 27ABCDE1234F1Z5 " + new string('x', 12000)),
            "text/plain", Email, Guid.NewGuid(), true, default);
        Assert.False(result.IsNotice);
        Assert.True(result.AiUsed);
        Assert.False(result.Uncertain);
        Assert.Equal(["/api/v1/email-notices/classify"], handler.Paths);
        Assert.True(handler.Body!.Length < 6500);
    }

    [Fact]
    public async Task ExhaustedBudgetRetainsUncertaintyWithoutCallingModel()
    {
        var handler = new Handler("{}");
        var result = await Inspector(handler).InspectAsync("GST Notice 27ABCDE1234F1Z5"u8.ToArray(), "text/plain", Email, Guid.NewGuid(), false, default);
        Assert.True(result.Uncertain);
        Assert.False(result.AiUsed);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task UnrelatedTextDoesNotInvokeModel()
    {
        var handler = new Handler("{}");
        var result = await Inspector(handler).InspectAsync("Monthly newsletter"u8.ToArray(), "text/plain", Email, Guid.NewGuid(), true, default);
        Assert.False(result.IsNotice);
        Assert.False(result.Uncertain);
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 3,6 * * *")]
    public void FrequentMailboxSchedulesAreRejected(string schedule)
    {
        var options = new EmailSyncOptions { SyncScheduleCron = schedule };
        Assert.NotEmpty(options.Validate(new ValidationContext(options)));
    }

    [Fact]
    public void MailboxWriteScopesAreRejected()
    {
        var options = new EmailSyncOptions();
        options.Outlook.Scopes.Add("Mail.ReadWrite");
        Assert.NotEmpty(options.Validate(new ValidationContext(options)));
    }

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
