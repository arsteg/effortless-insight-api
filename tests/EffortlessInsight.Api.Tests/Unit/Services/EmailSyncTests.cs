using System.Net;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.EmailSync;
using EffortlessInsight.Api.Services.Encryption;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Storage;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class EmailSyncTests : IDisposable
{
    private readonly ApplicationDbContext db;
    private readonly Mock<IEmailProviderClient> provider = new();
    private readonly Mock<IEmailOAuthAttemptStore> attempts = new();
    private readonly Mock<IEmailOperationLock> locks = new();
    private readonly Mock<INoticeServiceExtended> notices = new();
    private readonly Mock<IUsageService> usage = new();
    private readonly Mock<IEmailNoticeInspector> inspector = new();
    private readonly IFieldEncryptionService encryption;
    private readonly EmailSyncOptions options = new() { PublicApiBaseUrl = "https://api.example.test", AllowedReturnUris = ["https://app.example.test/settings/email-sync"] };
    private readonly Guid org = Guid.NewGuid(), user = Guid.NewGuid();

    public EmailSyncTests()
    {
        encryption = new FieldEncryptionService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Encryption:FieldEncryptionKey"] = "tests-only-encryption-key-with-32-characters" }).Build(), NullLogger<FieldEncryptionService>.Instance);
        FieldEncryptionServiceAccessor.SetInstance(encryption);
        db = BillingTestDbContextFactory.Create();
        provider.SetupGet(p => p.Provider).Returns("gmail");
        locks.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new Handle());
        usage.Setup(u => u.CanCreateNoticeAsync(org)).ReturnsAsync((true, (string?)null));
        options.Gmail.ClientId = options.Gmail.ClientSecret = "test";
        options.Outlook.ClientId = options.Outlook.ClientSecret = "test";
    }

    private EmailSyncService Service() => new(db, notices.Object, locks.Object, inspector.Object, [provider.Object],
        new NoticeDetectionService(Microsoft.Extensions.Options.Options.Create(options), NullLogger<NoticeDetectionService>.Instance), encryption,
        Mock.Of<IFileStorageService>(), usage.Object, Mock.Of<IAuditService>(), attempts.Object,
        Microsoft.Extensions.Options.Options.Create(options), NullLogger<EmailSyncService>.Instance);

    private ConnectedEmailAccount Account()
    {
        var account = new ConnectedEmailAccount { OrganizationId = org, UserId = user, Email = "owner@example.test",
            Provider = "gmail", AccessTokenEncrypted = encryption.Encrypt("access"), RefreshTokenEncrypted = encryption.Encrypt("refresh"),
            TokenExpiresAt = DateTime.UtcNow.AddHours(2), LastSyncCursor = new EmailScanCursor(DateTime.UtcNow.Date.AddDays(-1), SenderPolicyHash: EmailScanCursor.PolicyHash(options.AllowedSenders)).Encode() };
        db.ConnectedEmailAccounts.Add(account); db.SaveChanges(); return account;
    }

    [Theory]
    [InlineData("notice@gst.gov.in", true)]
    [InlineData("GST <notice@GST.GOV.IN>", true)]
    [InlineData("notice@gst.gov.in.attacker.test", false)]
    [InlineData("gst.gov.in@attacker.test", false)]
    [InlineData("notice@other.gov.in", false)]
    public void SenderAllowlistUsesExactDomain(string sender, bool expected) => Assert.Equal(expected, EmailSenderPolicy.IsAllowed(sender, ["@gst.gov.in"]));

    [Fact]
    public async Task InvalidStateNeverExchangesCode()
    {
        var result = await Service().CompleteOAuthAsync(org, user, "gmail", "code", "expired", "https://evil.test");
        Assert.Equal("invalid_state", result.ErrorCode);
        provider.Verify(p => p.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("gmail")]
    [InlineData("outlook")]
    public async Task CallbackUsesStoredUriAndVerifierAndEncryptsTokens(string name)
    {
        provider.SetupGet(p => p.Provider).Returns(name);
        db.OrganizationMembers.Add(new() { OrganizationId = org, UserId = user, Role = "owner", Status = "active" }); await db.SaveChangesAsync();
        var attempt = new EmailOAuthAttempt(org, user, name, "https://api.example.test/callback", "https://app.example.test/settings/email-sync", "secret-verifier");
        attempts.Setup(a => a.ConsumeAsync("state")).ReturnsAsync(attempt);
        provider.Setup(p => p.ExchangeCodeAsync("code", attempt.CallbackUri, attempt.Verifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OAuthTokenResult { Success = true, AccessToken = "access", RefreshToken = "refresh", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        provider.Setup(p => p.GetUserProfileAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync(new EmailUserProfile { Email = "Owner@example.test" });
        var result = await Service().CompleteOAuthAsync(Guid.Empty, Guid.Empty, name, "code", "state", "https://attacker.test");
        Assert.True(result.Success); Assert.Equal(attempt.ReturnUri, result.ReturnUri);
        Assert.NotEqual("access", result.Account!.AccessTokenEncrypted);
        Assert.Equal("refresh", encryption.Decrypt(result.Account.RefreshTokenEncrypted!));
        Assert.Equal("owner@example.test", result.Account.Email);
    }

    [Fact]
    public async Task UnregisteredReturnUriIsRejectedBeforeStoringState()
    {
        var result = await Service().InitiateOAuthAsync(org, user, "gmail", "https://attacker.test");
        Assert.Equal("redirect_not_configured", result.ErrorCode);
        attempts.Verify(a => a.CreateAsync(It.IsAny<EmailOAuthAttempt>()), Times.Never);
    }

    [Fact]
    public async Task RefreshFailureStopsMailboxAndRequiresReconnect()
    {
        var account = Account(); account.TokenExpiresAt = DateTime.UtcNow.AddMinutes(1); await db.SaveChangesAsync();
        provider.Setup(p => p.RefreshTokenAsync("refresh", It.IsAny<CancellationToken>())).ReturnsAsync(new OAuthTokenResult { ErrorCode = "invalid_grant" });
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.False(result.Success); Assert.Equal(EmailAccountStatus.ReauthenticationRequired, account.Status);
        provider.Verify(p => p.FetchEmailsAsync(It.IsAny<string>(), It.IsAny<EmailFetchOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshHappensBeforeExpiryAndRotatesRefreshToken()
    {
        var account = Account(); account.TokenExpiresAt = DateTime.UtcNow.AddMinutes(20); await db.SaveChangesAsync();
        provider.Setup(p => p.RefreshTokenAsync("refresh", It.IsAny<CancellationToken>())).ReturnsAsync(new OAuthTokenResult {
            Success = true, AccessToken = "new-access", RefreshToken = "new-refresh", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        Assert.True(await Service().RefreshTokenIfNeededAsync(account.Id));
        Assert.Equal("new-refresh", encryption.Decrypt(account.RefreshTokenEncrypted!));
    }

    [Fact]
    public async Task ScheduledScanRunsOnceButManualScanIsAllowed()
    {
        var account = Account();
        provider.Setup(p => p.FetchEmailsAsync(It.IsAny<string>(), It.IsAny<EmailFetchOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EmailFetchResult { Success = true, NextSyncCursor = "checkpoint" });
        await Service().SyncAccountAsync(account.Id, triggerSource: EmailSyncTrigger.Scheduled);
        await Service().SyncAccountAsync(account.Id, triggerSource: EmailSyncTrigger.Scheduled);
        await Service().SyncAccountAsync(account.Id, triggerSource: "manual");
        provider.Verify(p => p.FetchEmailsAsync(It.IsAny<string>(), It.IsAny<EmailFetchOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task QuotaDefersWorkAndKeepsCheckpointWithoutCallingAi()
    {
        var account = Account(); var before = account.LastSyncCursor;
        SetupMessage(); usage.Setup(u => u.CanCreateNoticeAsync(org)).ReturnsAsync((false, "Limit reached"));
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.Equal("monthly_limit_reached", result.ErrorCode); Assert.Equal(before, account.LastSyncCursor);
        Assert.Contains("monthly reset", result.ErrorMessage);
        inspector.Verify(i => i.InspectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingFileIsDuplicateEvenAtQuotaWithoutAiOrUpload()
    {
        var account = Account(); SetupMessage();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("pdf-bytes"u8)).ToLowerInvariant();
        db.Notices.Add(new Notice { OrganizationId = org, UploadedById = user, FileName = "existing.pdf", FileUrl = "stored", FileHash = hash }); await db.SaveChangesAsync();
        usage.Setup(u => u.CanCreateNoticeAsync(org)).ReturnsAsync((false, "Limit reached"));
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.Equal(1, result.DuplicatesSkipped); Assert.True(result.Success);
        Assert.Empty(db.EmailImportReceipts.Where(r => r.Outcome == "deferred"));
    }

    [Theory]
    [InlineData("duplicate", false)]
    [InlineData("processed", false)]
    [InlineData("duplicate", true)]
    public async Task DeletedNoticeReceiptIsRetriedUnlessAnActiveCopyExists(string outcome, bool activeCopy)
    {
        var account = Account(); SetupMessage();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("pdf-bytes"u8)).ToLowerInvariant();
        var deleted = new Notice { OrganizationId = org, UploadedById = user, FileName = "deleted.pdf",
            FileUrl = "stored", FileHash = hash, DeletedAt = DateTime.UtcNow };
        db.Notices.Add(deleted);
        if (activeCopy) db.Notices.Add(new Notice { OrganizationId = org, UploadedById = user,
            FileName = "active.pdf", FileUrl = "stored", FileHash = hash });
        db.EmailImportReceipts.Add(new() { OrganizationId = org, ConnectedEmailAccountId = account.Id,
            ItemKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("message\none"))).ToLowerInvariant(),
            Outcome = outcome, AttachmentHash = hash, NoticeId = outcome == "processed" ? deleted.Id : null });
        await db.SaveChangesAsync();
        // Reaching the quota gate proves deleted matches no longer terminate processing.
        usage.Setup(u => u.CanCreateNoticeAsync(org)).ReturnsAsync((false, "Limit reached"));
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.Equal(activeCopy ? 1 : 0, result.DuplicatesSkipped);
        Assert.Equal(activeCopy ? null : "monthly_limit_reached", result.ErrorCode);
        Assert.Equal(activeCopy ? outcome : "deferred", db.EmailImportReceipts.Single().Outcome);
    }

    [Fact]
    public async Task InspectionFailureReportsStageAndStatusWithoutLeakingProviderDetails()
    {
        var account = Account(); SetupMessage();
        var checkpoint = account.LastSyncCursor;
        inspector.Setup(i => i.InspectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<EmailMessage>(),
            org, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("private document and credentials", null, HttpStatusCode.NotFound));
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.False(result.Success);
        Assert.Contains("document_inspection/HttpRequestException/HTTP-404", result.ErrorMessage);
        Assert.Equal(checkpoint, account.LastSyncCursor);
        Assert.Equal("deferred", db.EmailImportReceipts.Single().Outcome);
        var log = db.EmailSyncLogs.Single();
        Assert.Contains("document_inspection", log.ErrorDetails);
        Assert.DoesNotContain("private document", log.ErrorDetails);
        Assert.DoesNotContain("private document", result.ErrorMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterPageIsProcessedAfterGstinMismatchAndQuotaHasPriority(bool quotaReached)
    {
        var account = Account(); SetupMessage();
        var originalCursor = account.LastSyncCursor;
        var next = new EmailScanCursor(DateTime.UtcNow.AddHours(-2)).Encode();
        var seen = new List<string?>();
        db.OrganizationGstins.Add(new() { OrganizationId = org, Gstin = "33AAACH7409R1Z8" });
        await db.SaveChangesAsync();
        EmailMessage Message(string id) => new() { MessageId = id, Sender = "n@gst.gov.in",
            Attachments = [new() { AttachmentId = "one", FileName = "notice.pdf", MimeType = "application/pdf" }] };
        provider.SetupSequence(p => p.FetchEmailsAsync(It.IsAny<string>(), It.IsAny<EmailFetchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailFetchResult { Success = true, HasMore = true, NextSyncCursor = next, Messages = [Message("wrong")] })
            .ReturnsAsync(new EmailFetchResult { Success = true, HasMore = false, NextSyncCursor = new EmailScanCursor(DateTime.UtcNow.AddMinutes(-10)).Encode(), Messages = [Message("right")] });
        provider.Setup(p => p.DownloadAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), "one", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentDownloadResult { Success = true, Data = "pdf-bytes"u8.ToArray() });
        inspector.Setup(i => i.InspectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<EmailMessage>(), org, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] data, string mime, EmailMessage email, Guid organization, bool ai, CancellationToken ct) =>
                new EmailInspection(true, false, false, [email.MessageId == "wrong" ? "06ACAFA7813K1ZG" : "33AAACH7409R1Z8"], data, false));
        if (quotaReached) usage.SetupSequence(u => u.CanCreateNoticeAsync(org))
            .ReturnsAsync((true, (string?)null)).ReturnsAsync((false, "Limit reached"));
        notices.Setup(n => n.UploadEmailAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), org, user,
            "33AAACH7409R1Z8", It.IsAny<CancellationToken>(), It.IsAny<EmailIngestionContext>()))
            .ReturnsAsync(new NoticeUploadResult { Success = true, NoticeId = Guid.NewGuid() });
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.Equal(2, result.EmailsScanned);
        Assert.Equal(quotaReached ? 0 : 1, result.NoticesImported);
        Assert.Equal(quotaReached ? "monthly_limit_reached" : "no_gstin_match", result.ErrorCode);
        Assert.Equal(originalCursor, account.LastSyncCursor);
        provider.Verify(p => p.FetchEmailsAsync(It.IsAny<string>(), It.Is<EmailFetchOptions>(o => o.SyncCursor == next), It.IsAny<CancellationToken>()), Times.Once);
        notices.Verify(n => n.UploadEmailAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), org, user,
            "33AAACH7409R1Z8", It.IsAny<CancellationToken>(), It.IsAny<EmailIngestionContext>()), quotaReached ? Times.Never() : Times.Once());
    }

    private void SetupMessage()
    {
        provider.Setup(p => p.FetchEmailsAsync(It.IsAny<string>(), It.IsAny<EmailFetchOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EmailFetchResult {
            Success = true, NextSyncCursor = "new-checkpoint", Messages = [new EmailMessage { MessageId = "message", Sender = "notice@gst.gov.in",
                Attachments = [new EmailAttachment { AttachmentId = "one", FileName = "notice.pdf", MimeType = "application/pdf", Size = 10 }] }] });
        provider.Setup(p => p.DownloadAttachmentAsync(It.IsAny<string>(), "message", "one", It.IsAny<CancellationToken>())).ReturnsAsync(new AttachmentDownloadResult { Success = true, Data = Encoding.UTF8.GetBytes("pdf-bytes") });
    }

    [Fact]
    public async Task SenderChangeRescansOnceThenKeepsNewCheckpoint()
    {
        var account = Account();
        var oldHash = EmailScanCursor.PolicyHash(options.AllowedSenders);
        account.LastSyncCursor = new EmailScanCursor(DateTime.UtcNow.AddHours(-1), SenderPolicyHash: oldHash).Encode();
        await db.SaveChangesAsync();
        options.AllowedSenders.Add("test@example.com");
        var seen = new List<EmailScanCursor>();
        var next = new EmailScanCursor(DateTime.UtcNow.AddMinutes(-10));
        provider.Setup(p => p.FetchEmailsAsync(It.IsAny<string>(), It.IsAny<EmailFetchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailFetchOptions, CancellationToken>((_, fetch, _) => seen.Add(EmailScanCursor.Read(fetch.SyncCursor, DateTime.UtcNow)))
            .ReturnsAsync(new EmailFetchResult { Success = true, NextSyncCursor = next.Encode() });
        Assert.True((await Service().SyncAccountAsync(account.Id)).Success);
        Assert.True((await Service().SyncAccountAsync(account.Id)).Success);
        Assert.Equal(DateTime.UtcNow.AddDays(-options.InitialSyncLookbackDays).Date, seen[0].Day);
        Assert.Equal(next.Day, seen[1].Day);
        Assert.Equal(EmailScanCursor.PolicyHash(options.AllowedSenders), seen[1].SenderPolicyHash);
    }

    [Fact]
    public async Task CallbackLockFailureReturnsSafeCodeAndTrustedReturnUri()
    {
        db.OrganizationMembers.Add(new() { OrganizationId = org, UserId = user, Role = "owner", Status = "active" });
        await db.SaveChangesAsync();
        var attempt = new EmailOAuthAttempt(org, user, "gmail", "https://api.example.test/callback", options.AllowedReturnUris[0], "verifier");
        attempts.Setup(a => a.ConsumeAsync("state")).ReturnsAsync(attempt);
        provider.Setup(p => p.ExchangeCodeAsync("code", attempt.CallbackUri, attempt.Verifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OAuthTokenResult { Success = true, AccessToken = "access", RefreshToken = "refresh", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        provider.Setup(p => p.GetUserProfileAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync(new EmailUserProfile { Email = "owner@example.test" });
        locks.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Npgsql.NpgsqlException("private credentials"));
        var result = await Service().CompleteOAuthAsync(Guid.Empty, Guid.Empty, "gmail", "code", "state", "https://attacker.test");
        Assert.False(result.Success);
        Assert.Equal("connection_lock_failed", result.ErrorCode);
        Assert.Equal(attempt.ReturnUri, result.ReturnUri);
        Assert.DoesNotContain("private credentials", result.ErrorMessage);
        Assert.Empty(db.ConnectedEmailAccounts);
    }

    [Fact]
    public async Task TwoAttachmentsInOneMessageUseSharedPipelineSeparately()
    {
        var account = Account(); SetupMessage();
        var next = new EmailScanCursor(DateTime.UtcNow.AddMinutes(-10));
        db.OrganizationGstins.Add(new() { OrganizationId = org, Gstin = "27ABCDE1234F1Z5" }); await db.SaveChangesAsync();
        provider.Setup(p => p.FetchEmailsAsync(It.IsAny<string>(), It.IsAny<EmailFetchOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EmailFetchResult {
            Success = true, NextSyncCursor = next.Encode(), Messages = [new() { MessageId = "message", Sender = "n@gst.gov.in", Attachments = [
                new() { AttachmentId = "one", FileName = "one.pdf", MimeType = "application/pdf", Size = 10 },
                new() { AttachmentId = "two", FileName = "two.pdf", MimeType = "application/pdf", Size = 10 }] }] });
        provider.Setup(p => p.DownloadAttachmentAsync(It.IsAny<string>(), "message", "two", It.IsAny<CancellationToken>())).ReturnsAsync(new AttachmentDownloadResult { Success = true, Data = "different-pdf"u8.ToArray() });
        inspector.Setup(i => i.InspectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<EmailMessage>(), org, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailInspection(true, false, false, ["27ABCDE1234F1Z5"], "pdf"u8.ToArray(), false));
        notices.Setup(n => n.UploadEmailAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), org, user, "27ABCDE1234F1Z5", It.IsAny<CancellationToken>(), It.IsAny<EmailIngestionContext>()))
            .ReturnsAsync(new NoticeUploadResult { Success = true, NoticeId = Guid.NewGuid() });
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.True(result.Success); Assert.Equal(2, result.NoticesImported);
        Assert.Equal(2, db.EmailImportReceipts.Count());
        Assert.Equal(next.Day, EmailScanCursor.Read(account.LastSyncCursor, DateTime.UtcNow).Day);
        notices.Verify(n => n.UploadEmailAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), org, user,
            "27ABCDE1234F1Z5", It.IsAny<CancellationToken>(), It.Is<EmailIngestionContext>(c => c.AccountId == account.Id && c.MessageId == "message")), Times.Exactly(2));
        await Service().SyncAccountAsync(account.Id);
        notices.Verify(n => n.UploadEmailAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), org, user,
            "27ABCDE1234F1Z5", It.IsAny<CancellationToken>(), It.IsAny<EmailIngestionContext>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UnmatchedGstinNeverEntersPipeline()
    {
        var account = Account(); SetupMessage();
        inspector.Setup(i => i.InspectAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<EmailMessage>(), org, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailInspection(true, false, false, ["27ABCDE1234F1Z5"], "pdf"u8.ToArray(), false));
        var result = await Service().SyncAccountAsync(account.Id);
        Assert.Equal("no_gstin_match", result.ErrorCode); Assert.Equal(0, result.NoticesImported);
        Assert.Contains("Detected: 27ABCDE1234F1Z5", result.ErrorMessage);
        Assert.Contains("0 non-deleted registrations", result.ErrorMessage);
        Assert.Contains(db.EmailImportReceipts.Single().Id.ToString(), result.ErrorMessage);
        Assert.Equal("deferred", db.EmailImportReceipts.Single().Outcome);
    }

    [Fact]
    public async Task AccountsAreScopedToOrganizationAndSupportMultipleEmails()
    {
        Account(); var second = Account(); second.Email = "second@example.test";
        var other = Account(); other.OrganizationId = Guid.NewGuid(); await db.SaveChangesAsync();
        var accounts = await Service().GetAccountsAsync(org);
        Assert.Equal(2, accounts.Count); Assert.DoesNotContain(accounts, a => a.Id == other.Id);
    }

    [Fact]
    public async Task ProviderUrlsRequireReadonlyScopesAndPkce()
    {
        using var http = new HttpClient(new Handler());
        var gmail = new GmailProviderClient(Microsoft.Extensions.Options.Options.Create(options), http, NullLogger<GmailProviderClient>.Instance);
        var outlook = new OutlookProviderClient(Microsoft.Extensions.Options.Options.Create(options), http, NullLogger<OutlookProviderClient>.Instance);
        foreach (var client in new IEmailProviderClient[] { gmail, outlook })
        {
            var url = client.GetAuthorizationUrl("state", "https://api.example.test/callback", "challenge");
            Assert.Contains("code_challenge=challenge", url); Assert.Contains("code_challenge_method=S256", url);
            Assert.DoesNotContain("Mail.ReadWrite", url); Assert.DoesNotContain("gmail.modify", url);
            var tokens = await client.ExchangeCodeAsync("code", "https://api.example.test/callback", "verifier", default);
            Assert.True(tokens.Success);
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("code_verifier=verifier", form); Assert.Contains("grant_type=authorization_code", form);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"expires_in\":3600}", Encoding.UTF8, "application/json") };
        }
    }

    public void Dispose() => db.Dispose();
    private sealed class Handle : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
