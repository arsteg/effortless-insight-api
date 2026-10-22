using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class StructuredDraftPersistenceTests
{
    [Fact]
    public async Task SavedStructureSurvivesReloadAndUnchangedSimpleSave()
    {
        await using var db = BillingTestDbContextFactory.Create();
        var org = Guid.NewGuid();
        var user = new ApplicationUser { Id = Guid.NewGuid(), Name = "Test user", UserName = "test@example.test" };
        var notice = new Notice { Id = Guid.NewGuid(), OrganizationId = org, UploadedById = user.Id };
        db.Users.Add(user);
        db.Notices.Add(notice);
        await db.SaveChangesAsync();
        var service = new NoticeServiceImpl(db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, NullLogger<NoticeServiceImpl>.Instance);
        const string json = """{"Allegations":[{"Id":"ALG-1"}],"Responses":[{"AllegationId":"ALG-1","ResponseText":"Edited reply"}]}""";
        await service.SaveResponseDraftAsync(notice.Id, org, user.Id, "Edited reply", default, json);
        db.ChangeTracker.Clear();
        var restored = await service.GetLatestResponseAsync(notice.Id, org);
        Assert.NotNull(restored);
        Assert.Equal(json, restored.AllegationDraftJson);
        await service.SaveResponseDraftAsync(notice.Id, org, user.Id, "Edited reply");
        db.ChangeTracker.Clear();
        restored = await service.GetLatestResponseAsync(notice.Id, org);
        Assert.Equal(json, restored!.AllegationDraftJson);
        Assert.Equal("Edited reply", restored.DraftContent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLatestResponseAsync(notice.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task EditedSimpleContentDoesNotRestoreStaleAllegationCards()
    {
        await using var db = BillingTestDbContextFactory.Create();
        var org = Guid.NewGuid();
        var user = new ApplicationUser { Id = Guid.NewGuid(), Name = "Test user", UserName = "test@example.test" };
        var notice = new Notice { Id = Guid.NewGuid(), OrganizationId = org, UploadedById = user.Id };
        db.Users.Add(user);
        db.Notices.Add(notice);
        await db.SaveChangesAsync();
        var service = new NoticeServiceImpl(db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, NullLogger<NoticeServiceImpl>.Instance);
        await service.SaveResponseDraftAsync(notice.Id, org, user.Id, "Old content", default, "{}");
        await service.SaveResponseDraftAsync(notice.Id, org, user.Id, "New free-text reply");
        db.ChangeTracker.Clear();
        var restored = await service.GetLatestResponseAsync(notice.Id, org);
        Assert.Equal("New free-text reply", restored!.DraftContent);
        Assert.Null(restored.AllegationDraftJson);
    }
}
