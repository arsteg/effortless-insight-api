using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Collaboration;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Storage;
using EffortlessInsight.Api.Tests.Helpers;
using Hangfire;
using NoticeProcessingStatus = EffortlessInsight.Api.Data.Entities.NoticeProcessingStatus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class NoticeProcessingIdentityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractedDuplicateLeavesOnlyOneActiveNotice(bool existingFromSync)
    {
        using var db = BillingTestDbContextFactory.Create();
        var org = Guid.NewGuid();
        var existing = NoticeIdentityTests.NewNotice(org);
        existing.Source = existingFromSync ? NoticeSource.GstnPortal : NoticeSource.Manual;
        var upload = NoticeIdentityTests.NewNotice(org, null, null);
        db.Notices.AddRange(existing, upload);
        db.OrganizationGstins.Add(new OrganizationGstin { OrganizationId = org, Gstin = existing.Gstin!, StateCode = "27", StateName = "Maharashtra" });
        await db.SaveChangesAsync();
        var usage = new Mock<IUsageService>();
        var job = CreateJob(db, existing.Gstin!, usage);
        await job.ProcessAsync(upload.Id, default);
        Assert.Equal(existing.Id, (await db.Notices.SingleAsync()).Id);
        var loser = await db.Notices.IgnoreQueryFilters().SingleAsync(n => n.Id == upload.Id);
        Assert.NotNull(loser.DeletedAt);
        Assert.Contains("DUPLICATE_NOTICE", loser.ProcessingError);
        usage.Verify(u => u.DecrementNoticeCountAsync(org), Times.Once);
        // Retried delivery does not decrement twice.
        await job.ProcessAsync(upload.Id, default);
        usage.Verify(u => u.DecrementNoticeCountAsync(org), Times.Once);
    }

    [Theory]
    [InlineData("27AABCU9603R1ZN", true)]
    [InlineData("29AABCU9603R1ZN", false)]

    public async Task BusinessOwnerExtractionMustUseRegisteredGstin(string detectedGstin, bool allowed)
    {
        using var db = BillingTestDbContextFactory.Create();
        var upload = NoticeIdentityTests.NewNotice(Guid.NewGuid(), null, null);
        db.Notices.Add(upload);
        db.OrganizationGstins.Add(new OrganizationGstin { OrganizationId = upload.OrganizationId,
            Gstin = "27AABCU9603R1ZN", StateCode = "27", StateName = "Maharashtra" });
        await db.SaveChangesAsync();
        await CreateJob(db, detectedGstin, new Mock<IUsageService>()).ProcessAsync(upload.Id, default);
        db.ChangeTracker.Clear();
        var saved = await db.Notices.SingleAsync();
        Assert.Equal(allowed ? NoticeProcessingStatus.Completed : NoticeProcessingStatus.Failed, saved.ProcessingStatus);
        if (!allowed)
        {
            Assert.Contains("INVALID_GSTIN", saved.ProcessingError);
            Assert.Equal(1, saved.ProcessingAttempts);
        }
        else Assert.Equal(detectedGstin, saved.Gstin);
    }

    [Fact]
    public async Task ExistingRegisteredTestGstinIsNotRejectedByNewFormatRules()
    {
        using var db = BillingTestDbContextFactory.Create();
        var notice = NoticeIdentityTests.NewNotice(Guid.NewGuid(), null, "33AAACH7409RZ8");
        db.Notices.Add(notice);
        db.OrganizationGstins.Add(new OrganizationGstin { OrganizationId = notice.OrganizationId,
            Gstin = "33AAACH7409RZ8", StateCode = "33", StateName = "Tamil Nadu" });
        await db.SaveChangesAsync();
        await CreateJob(db, "33AAACH7409RZ8", new Mock<IUsageService>()).ProcessAsync(notice.Id, default);
        db.ChangeTracker.Clear();
        var saved = await db.Notices.SingleAsync();
        Assert.Equal(NoticeProcessingStatus.Completed, saved.ProcessingStatus);
        Assert.Null(saved.ProcessingError);
    }

    [Fact]
    public async Task CorrectedValidationFailureCanRetryAfterOldRetryLimit()
    {
        using var db = BillingTestDbContextFactory.Create();
        var notice = NoticeIdentityTests.NewNotice(Guid.NewGuid());
        notice.ProcessingStatus = NoticeProcessingStatus.Failed;
        notice.ProcessingError = "INVALID_GSTIN: The document GSTIN is not registered in this organization.";
        notice.ProcessingAttempts = 3;
        db.Notices.Add(notice); await db.SaveChangesAsync();
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>())).Returns("retry-job");
        var service = new NoticeServiceImpl(db, null!, null!, null!, null!, jobs.Object, null!, null!, null!, null!, null!,
            NullLogger<NoticeServiceImpl>.Instance);
        Assert.Equal("retry-job", await service.RetryProcessingAsync(notice.Id, notice.OrganizationId));
        Assert.Equal(NoticeProcessingStatus.Queued, notice.ProcessingStatus);
        Assert.Equal(0, notice.ProcessingAttempts);
        Assert.Null(notice.ProcessingError);
    }

    [Fact]
    public void PublicErrorDoesNotExposeInfrastructureDetails()
    {
        var message = NoticeProcessingFailure.UserMessage("NpgsqlException: Host=private-server;Password=secret");
        Assert.DoesNotContain("secret", message);
        Assert.Contains("contact support", message);
        Assert.Contains("registered", NoticeProcessingFailure.UserMessage("INVALID_GSTIN: rejected"));
    }

    private static NoticeProcessingJob CreateJob(EffortlessInsight.Api.Data.ApplicationDbContext db, string gstin, Mock<IUsageService> usage)
    {
        var ai = new Mock<IAiServiceClient>();
        var metadata = new NoticeMetadata("DRC-01", "demand", "REF-123", gstin, null, null, null, null, null, null, null, null);
        ai.Setup(a => a.ProcessNoticeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new AiProcessingResult(true, null, new AiReportData(10, "low", "summary", "summary", "text", metadata, [], [], [], [])));
        var storage = new Mock<IFileStorageServiceExtended>();
        storage.Setup(s => s.GenerateDownloadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedDownloadResult("https://example.invalid/notice.pdf", DateTime.UtcNow.AddMinutes(30)));
        var workflow = new Mock<INoticeWorkflowService>();
        workflow.Setup(w => w.GetStatusAfterProcessing(true)).Returns(NoticeStatus.Analyzed);
        workflow.Setup(w => w.GetStatusAfterProcessing(false)).Returns(NoticeStatus.Failed);
        return new NoticeProcessingJob(db, ai.Object, storage.Object, workflow.Object,
            Mock.Of<IAuditService>(), Mock.Of<IBackgroundJobClient>(), Mock.Of<INotificationService>(),
            Mock.Of<INoticeBroadcastService>(), NullLogger<NoticeProcessingJob>.Instance, usage.Object);
    }
}
