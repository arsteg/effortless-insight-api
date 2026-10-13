using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.GstSync;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Storage;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class NoticeIngestionDuplicateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatUploadRejectsBeforeQuotaReservation(bool presigned)
    {
        using var db = BillingTestDbContextFactory.Create();
        var existing = NoticeIdentityTests.NewNotice(Guid.NewGuid()); existing.FileHash = "same-content";
        db.Notices.Add(existing); await db.SaveChangesAsync();
        var storage = new Mock<IFileStorageServiceExtended>();
        storage.Setup(s => s.ConfirmUploadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var validation = new Mock<IFileValidationService>();
        validation.Setup(v => v.ValidateAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileValidationResult.Success("application/pdf", "renamed.pdf", 10, existing.FileHash));
        var usage = new Mock<IUsageService>(MockBehavior.Strict);
        var service = new NoticeServiceImpl(db, storage.Object, validation.Object, null!, null!, null!, usage.Object,
            null!, null!, null!, null!, NullLogger<NoticeServiceImpl>.Instance);
        var result = presigned
            ? await service.ConfirmUploadAsync("uploaded-key", "renamed.pdf", "application/pdf", 10, existing.FileHash, existing.OrganizationId, existing.UploadedById)
            : await service.UploadAsync(new MemoryStream([1, 2, 3]), "renamed.pdf", "application/pdf", existing.OrganizationId, existing.UploadedById);
        Assert.False(result.Success);
        Assert.Equal("DUPLICATE_NOTICE", result.ErrorCode);
        Assert.Equal(1, await db.Notices.CountAsync());
        usage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(NoticeSource.Upload)]
    [InlineData(NoticeSource.GstnPortal)]
    public async Task SyncLinksToExistingNoticeAndRepeatSyncIsIgnored(string source)
    {
        using var db = BillingTestDbContextFactory.Create();
        var existing = NoticeIdentityTests.NewNotice(Guid.NewGuid()); existing.Source = source;
        var client = new GstClient { OrganizationId = existing.OrganizationId, Gstin = existing.Gstin!, CreatedByUserId = existing.UploadedById };
        var raw = new GstNoticeRaw { OrganizationId = existing.OrganizationId, GstClientId = client.Id,
            Gstin = existing.Gstin!, ReferenceNumber = " ref-123 ", PortalNoticeId = "portal-id", NoticeType = "DRC-01" };
        db.Notices.Add(existing); db.GstClients.Add(client); db.GstNoticesRaw.Add(raw); await db.SaveChangesAsync();
        var usage = new Mock<IUsageService>(MockBehavior.Strict);
        var service = new GstNoticeRawService(db, null!, null!, null!, null!, null!, null!, usage.Object,
            NullLogger<GstNoticeRawService>.Instance);
        for (var i = 0; i < 2; i++)
        {
            var result = await service.ImportNoticesAsync(existing.OrganizationId, existing.UploadedById,
                new ImportNoticesRequest { NoticeIds = [raw.Id] });
            Assert.Equal(1, result.AlreadyImported); Assert.Equal(0, result.Failed); Assert.Equal(0, result.Imported);
            Assert.Equal(existing.Id, raw.ImportedNoticeId);
            Assert.Equal(1, await db.Notices.CountAsync());
        }
        usage.VerifyNoOtherCalls();
    }
}
